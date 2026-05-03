/*
 * SOFAR ME3000SP — UDP Grid Receiver + Auto-Regulation (ESP32)
 * STABILITY-HARDENED VERSION
 *
 * Key stability rules:
 *   1. UDP gap → HOLD current command, do NOT change anything until
 *      GRID_HOLD_MS have passed with no data. Only then go standby.
 *   2. State transitions require CONFIRM_TICKS consecutive readings
 *      agreeing before the state actually changes. No flapping.
 *   3. Re-tune power only when delta exceeds RETUNE_DEADBAND_W.
 *      Prevents constant Modbus spam on tiny grid fluctuations.
 *   4. Minimum dwell time (MIN_DWELL_MS) before any state can change.
 *   5. Last sent command is tracked — never re-send identical commands.
 *
 * Wattrouter priority (unchanged):
 *   SOFAR charges ONLY when wattrouter both-relay saturated + surplus left.
 *   SOFAR discharges freely on deficit (no conflict).
 *   SOFAR stands by while wattrouter is still regulating.
 *
 * UDP packet: "G<watts>,P<pct>,R<0|1>"
 * Serial: c / d / s / a / p / h
 *
 * OLED: 4 rotating pages, yellow band header aware.
 */

#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <WiFi.h>
#include <WiFiUDP.h>
#include <ctype.h>
#include <stdlib.h>
#include <math.h>

// =============================================================
//  WiFi + UDP
// =============================================================
#define WIFI_SSID     "CHANGE_ME_WIFI_SSID"
#define WIFI_PASS     "CHANGE_ME_WIFI_PASSWORD"
#define UDP_PORT      4210

// =============================================================
//  STABILITY PARAMETERS  ← tune these
// =============================================================

// How long to HOLD the current inverter command after the last
// UDP packet before deciding data is truly gone and going standby.
// 30 s is safe: wattrouter broadcasts every 200 ms normally.
#define GRID_HOLD_MS          30000UL

// After GRID_HOLD_MS with no data, wait this long in standby before
// allowing regulation to start again when data returns.
// Prevents rapid standby↔regulate flapping on a flaky network.
#define GRID_RESUME_WAIT_MS    5000UL

// Number of consecutive AUTO_REG ticks (each AUTO_REG_INTERVAL_MS)
// that must agree on a NEW state before we actually switch.
// At 500 ms intervals, 4 = 2 seconds of agreement required.
#define CONFIRM_TICKS             4

// Only re-send a charge/discharge command if the target watts
// changed by more than this since the last command sent.
// Prevents Modbus spam on tiny grid fluctuations.
#define RETUNE_DEADBAND_W       100

// Minimum milliseconds to stay in any state before being allowed to leave.
#define MIN_DWELL_MS           3000UL

// =============================================================
//  REGULATION PARAMETERS
// =============================================================
#define CHARGE_ENGAGE_SURPLUS_W       200
#define CHARGE_DISENGAGE_SURPLUS_W     50
#define DISCHARGE_ENGAGE_DEFICIT_W    300
#define DISCHARGE_DISENGAGE_DEFICIT_W 100
#define WATTROUTER_SETPOINT_W         400
#define MANUAL_OVERRIDE_S              60
#define AUTO_REG_INTERVAL_MS          500

// =============================================================
//  OLED
// =============================================================
#define SCREEN_WIDTH  128
#define SCREEN_HEIGHT  64
#define SDA_PIN        21
#define SCL_PIN        22
#define OLED_ADDR    0x3C
#define OLED_RESET     -1
#define YELLOW_H       16
#define BLUE_Y         16

Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);

#define NUM_PAGES       4
#define PAGE_ROTATE_MS  4000UL

static uint8_t  currentPage    = 0;
static uint32_t lastPageChange = 0;

// =============================================================
//  RS485 / UART
// =============================================================
#define RTS_PIN 5
#define RX_PIN  17
#define TX_PIN  16

// =============================================================
//  Modbus / SOFAR
// =============================================================
static const uint8_t  SLAVE_ID = 1;
static const uint32_t BAUD     = 9600;

#define MODBUS_FN_READ_HOLDING 0x03
#define SOFAR_FN_PASSIVEMODE   0x42
#define SOFAR_FN_HEARTBEAT     0x49
#define SOFAR_PM_STANDBY       0x0100
#define SOFAR_PM_DISCHARGE     0x0101
#define SOFAR_PM_CHARGE        0x0102
#define SOFAR_PM_AUTO          0x0103
#define SOFAR_MAGIC_5555       0x5555

static const uint16_t MAX_WATTS = 3000;

// =============================================================
//  Battery pack
// =============================================================
#define PACK_SERIES      16
#define PACK_CAPACITY_AH 200
#define PACK_ENERGY_KWH  10.24f
static const float R_PACK_OHM = 0.020f;
// 1 = positive battery power means DISCHARGING. 0 = positive means CHARGING.
#define BAT_POWER_POSITIVE_DISCHARGE 1

// =============================================================
//  Statistics
// =============================================================
static uint32_t commandsSent      = 0;
static uint32_t commandsSuccess   = 0;
static uint32_t heartbeatsSent    = 0;
static uint32_t heartbeatsSuccess = 0;

// =============================================================
//  Inverter state (read from Modbus)
// =============================================================
static uint16_t currentState          = 0;
static uint16_t currentSOC            = 0;
static int32_t  currentBatteryPower   = 0;
static int32_t  currentGridPower      = 0;
static float    currentBatteryVoltage = 0;

static String   currentMode        = "STANDBY";
static String   modeShort          = "STBY";
static bool     lastCommandSuccess = true;
static bool     commError          = false;
static uint32_t lastCmdMs          = 0;

// =============================================================
//  My SOC (voltage-based)
// =============================================================
static uint8_t  mySocPct      = 0;
static float    mySocFilt     = NAN;
static uint32_t lastGoodCommMs = 0;

// =============================================================
//  UDP / Wattrouter state
// =============================================================
static int32_t  udpGridW          = 0;
static float    udpWattrPct       = 0.0f;
static bool     udpWattrSaturated = false;
static uint32_t lastGridRxMs      = 0;
static bool     gridDataFresh     = false;  // true once first packet arrives
static bool     gridDataHeld      = false;  // true while holding stale data
static uint32_t gridDataGoneMs    = 0;      // when data first went stale
static bool     waitingForResume  = false;  // post-stale settle gate

// =============================================================
//  Auto-regulation state machine
// =============================================================
enum AutoRegState { AR_STANDBY, AR_CHARGING, AR_DISCHARGING };
static AutoRegState arState        = AR_STANDBY;
static uint32_t     lastAutoRegMs  = 0;
static uint32_t     stateEnteredMs = 0;  // when we entered current arState

// Transition confirmation
static AutoRegState pendingState   = AR_STANDBY;
static uint8_t      confirmCount   = 0;

// Last actually-sent command (to enforce deadband)
static AutoRegState lastSentState  = AR_STANDBY;
static uint16_t     lastSentWatts  = 0;

// WiFiUDP
WiFiUDP udp;

// =============================================================
//  CRC
// =============================================================
static uint16_t calculateCRC(uint8_t *data, uint8_t len) {
  uint16_t crc = 0xFFFF;
  for (uint8_t i = 0; i < len; i++) {
    crc ^= data[i];
    for (uint8_t j = 0; j < 8; j++) {
      if (crc & 1) { crc >>= 1; crc ^= 0xA001; } else crc >>= 1;
    }
  }
  return crc;
}

// =============================================================
//  RS485
// =============================================================
static void rs485TxBegin() { digitalWrite(RTS_PIN, HIGH); delay(5); }
static void rs485TxEnd()   { delay(5); digitalWrite(RTS_PIN, LOW); delay(10); }

static bool sendFrame(const uint8_t* f, size_t n) {
  while (Serial2.available()) Serial2.read();
  rs485TxBegin(); Serial2.write(f, n); Serial2.flush(); rs485TxEnd();
  return true;
}

static bool receiveBytes(uint8_t* buf, size_t n, uint32_t tms) {
  uint32_t t0 = millis(); size_t got = 0;
  while (got < n && millis()-t0 < tms)
    if (Serial2.available()) buf[got++] = Serial2.read();
  return got == n;
}

// =============================================================
//  Math / format helpers
// =============================================================
static float clampf(float x, float a, float b) {
  return x < a ? a : x > b ? b : x;
}

static float socFromCellOCV(float v) {
  static const float V[] = {2.90f,3.05f,3.12f,3.18f,3.22f,3.26f,3.30f,3.33f,3.36f,3.40f,3.45f};
  static const float S[] = {0,5,12,22,38,60,78,86,93,97,100};
  if (v <= V[0])  return 0;
  if (v >= V[10]) return 100;
  for (int i = 0; i < 10; i++)
    if (v <= V[i+1]) return S[i] + (v-V[i])/(V[i+1]-V[i])*(S[i+1]-S[i]);
  return 50;
}

static uint8_t computeMySoc(float vPack, int32_t pBatW) {
  if (vPack < 10) return mySocPct;
  float p = (float)pBatW, I = fabsf(p)/vPack, vOCV = vPack;
  if (fabsf(p) > 150 && I < 250) {
    float sag = I * R_PACK_OHM;
#if BAT_POWER_POSITIVE_DISCHARGE
    vOCV = (p > 0) ? vPack+sag : vPack-sag;
#else
    vOCV = (p < 0) ? vPack+sag : vPack-sag;
#endif
  }
  float raw = clampf(socFromCellOCV(vOCV/PACK_SERIES), 0, 100);
  mySocFilt = isnan(mySocFilt) ? raw : mySocFilt*0.9f + raw*0.1f;
  return (uint8_t)(int)clampf((float)lroundf(mySocFilt), 0, 100);
}

static void formatDurationShort(uint32_t ms, char* out, size_t n) {
  if (ms == 0xFFFFFFFFu) { snprintf(out,n,"--"); return; }
  uint32_t s=ms/1000, m=s/60, h=m/60, d=h/24;
  s%=60; m%=60; h%=24;
  if (d)      snprintf(out,n,"%ud%uh",(unsigned)d,(unsigned)h);
  else if (h) snprintf(out,n,"%uh%um",(unsigned)h,(unsigned)m);
  else if (m) snprintf(out,n,"%um%us",(unsigned)m,(unsigned)s);
  else        snprintf(out,n,"%us",(unsigned)s);
}

static void formatPower(int32_t w, char* out, size_t n) {
  char sign = w>=0 ? '+' : '-';
  int32_t a = w>=0 ? w : -w;
  if (a >= 1000) {
    int32_t k = (a+50)/100;
    snprintf(out, n, "%c%ld.%ldkW", sign, (long)(k/10), (long)(k%10));
  } else {
    snprintf(out, n, "%c%ldW", sign, (long)a);
  }
}

// Large display version — avoids decimal-dot rendering bug at textSize 3
static void formatPowerLarge(int32_t w, char* valBuf, size_t vn, char* unitBuf, size_t un) {
  int32_t a = w>=0 ? w : -w;
  if (a >= 1000) {
    int32_t kw  = a / 1000;
    int32_t dec = (a % 1000 + 50) / 100;
    if (dec >= 10) { kw++; dec = 0; }
    snprintf(valBuf, vn, "%ld.%ld", (long)kw, (long)dec);
    snprintf(unitBuf, un, "kW");
  } else {
    snprintf(valBuf, vn, "%ld", (long)a);
    snprintf(unitBuf, un, "W");
  }
}

static bool isDischarging() {
#if BAT_POWER_POSITIVE_DISCHARGE
  return currentBatteryPower > 100;
#else
  return currentBatteryPower < -100;
#endif
}

static bool isCharging() {
#if BAT_POWER_POSITIVE_DISCHARGE
  return currentBatteryPower < -100;
#else
  return currentBatteryPower > 100;
#endif
}

static const char* stateName(uint16_t st) {
  static const char* s[]={"Wait","Check","Normal","ChkDis","Disch","EPS","Fault","PermFlt"};
  return st<8 ? s[st] : "Unk";
}

static const char* arStateName(AutoRegState s) {
  switch(s) { case AR_CHARGING: return "CHG"; case AR_DISCHARGING: return "DIS"; default: return "WAIT"; }
}

static void clampWatts(uint16_t &w) { if(w>MAX_WATTS) w=MAX_WATTS; }

// =============================================================
//  Modbus
// =============================================================
static bool readHoldingRegister(uint16_t reg, uint16_t &val) {
  uint8_t f[8] = {SLAVE_ID, MODBUS_FN_READ_HOLDING, (uint8_t)(reg>>8), (uint8_t)reg, 0, 1};
  uint16_t crc = calculateCRC(f,6); f[6]=crc&0xFF; f[7]=crc>>8;
  sendFrame(f,8);
  uint8_t r[7];
  if (!receiveBytes(r,7,800)||r[0]!=SLAVE_ID||r[1]!=MODBUS_FN_READ_HOLDING) return false;
  val = (uint16_t(r[3])<<8)|r[4];
  return true;
}

// ---------------------------------------------------------------
//  sendPassiveCommand — raw Modbus send, no dedup logic here
// ---------------------------------------------------------------
static bool sendPassiveCommand(uint16_t reg, uint16_t val) {
  uint8_t f[8] = {SLAVE_ID, SOFAR_FN_PASSIVEMODE,
                  (uint8_t)(reg>>8), (uint8_t)reg,
                  (uint8_t)(val>>8), (uint8_t)val};
  uint16_t crc = calculateCRC(f,6); f[6]=crc&0xFF; f[7]=crc>>8;
  sendFrame(f,8); commandsSent++; lastCmdMs=millis();
  uint8_t r[7];
  if (!receiveBytes(r,7,800)||r[0]!=SLAVE_ID||r[1]!=SOFAR_FN_PASSIVEMODE) {
    lastCommandSuccess=false; return false;
  }
  uint16_t sw = (uint16_t(r[3])<<8)|r[4];
  if ((sw&0xFF)==0) { commandsSuccess++; lastCommandSuccess=true; Serial.println("✓"); return true; }
  lastCommandSuccess=false; Serial.printf("✗ code %u\n", sw&0xFF); return false;
}

static bool sendHeartbeat() {
  uint8_t f[8] = {SLAVE_ID, SOFAR_FN_HEARTBEAT, 0x22, 0x01, 0x22, 0x02, 0, 0};
  uint16_t crc = calculateCRC(f,6); f[6]=crc&0xFF; f[7]=crc>>8;
  sendFrame(f,8); heartbeatsSent++;
  uint8_t r[7];
  if (!receiveBytes(r,7,800)) return false;
  heartbeatsSuccess++; lastGoodCommMs=millis(); return true;
}

// =============================================================
//  High-level inverter commands (with dedup guard)
//  For auto-regulation: setChargeAuto / setDischargeAuto only
//  send Modbus when state or watts changed beyond deadband.
//  setStandby / setCharge / setDischarge always send (manual + transitions).
// =============================================================
static bool setStandby() {
  currentMode="STANDBY"; modeShort="STBY";
  lastSentState=AR_STANDBY; lastSentWatts=0;
  Serial.println("→ STANDBY");
  return sendPassiveCommand(SOFAR_PM_STANDBY, SOFAR_MAGIC_5555);
}

static bool setCharge(uint16_t w) {
  clampWatts(w);
  currentMode="CHARGE "+String(w)+"W";
  if (w>=1000) { int k=(w+50)/100; modeShort="CHG"+String(k/10)+"."+String(k%10)+"k"; }
  else         { modeShort="CHG"+String(w); }
  lastSentState=AR_CHARGING; lastSentWatts=w;
  Serial.printf("→ CHARGE %uW\n",w);
  return sendPassiveCommand(SOFAR_PM_CHARGE, w);
}

static bool setDischarge(uint16_t w) {
  clampWatts(w);
  currentMode="DISCHARGE "+String(w)+"W";
  if (w>=1000) { int k=(w+50)/100; modeShort="DIS"+String(k/10)+"."+String(k%10)+"k"; }
  else         { modeShort="DIS"+String(w); }
  lastSentState=AR_DISCHARGING; lastSentWatts=w;
  Serial.printf("→ DISCHARGE %uW\n",w);
  return sendPassiveCommand(SOFAR_PM_DISCHARGE, w);
}

// ---------------------------------------------------------------
//  setChargeAuto / setDischargeAuto
//  Only send Modbus if: state changed, OR watts delta > deadband.
//  Returns true if a command was actually sent.
// ---------------------------------------------------------------
static bool setChargeAuto(uint16_t w) {
  clampWatts(w); if (w < 50) w = 50;
  int32_t delta = (int32_t)w - (int32_t)lastSentWatts;
  if (delta < 0) delta = -delta;
  if (lastSentState == AR_CHARGING && delta <= RETUNE_DEADBAND_W) {
    return false;  // no change worth sending
  }
  return setCharge(w);
}

static bool setDischargeAuto(uint16_t w) {
  clampWatts(w); if (w < 50) w = 50;
  int32_t delta = (int32_t)w - (int32_t)lastSentWatts;
  if (delta < 0) delta = -delta;
  if (lastSentState == AR_DISCHARGING && delta <= RETUNE_DEADBAND_W) {
    return false;
  }
  return setDischarge(w);
}

// ---------------------------------------------------------------
//  transitionTo — confirmed state change with dwell guard
//  Call with desired NEW state each tick. Switches only after
//  CONFIRM_TICKS consecutive ticks AND MIN_DWELL_MS elapsed.
// ---------------------------------------------------------------
static void transitionTo(AutoRegState desired) {
  uint32_t now = millis();

  if (desired == arState) {
    // Happy in current state — reset pending counter
    pendingState = arState;
    confirmCount = 0;
    return;
  }

  // Enforce minimum dwell time before leaving current state
  if (now - stateEnteredMs < MIN_DWELL_MS) {
    pendingState = arState;
    confirmCount = 0;
    return;
  }

  // Count consecutive ticks wanting the same new state
  if (desired == pendingState) {
    confirmCount++;
  } else {
    pendingState = desired;
    confirmCount = 1;
  }

  if (confirmCount >= CONFIRM_TICKS) {
    Serial.printf("[AR] %s -> %s (confirmed)\n", arStateName(arState), arStateName(desired));
    arState       = desired;
    stateEnteredMs = now;
    confirmCount  = 0;
    pendingState  = desired;
  }
}

// =============================================================
//  updateReadings
// =============================================================
void updateReadings() {
  commError = false;
  if (!readHoldingRegister(0x0200, currentState))  commError = true;
  if (!readHoldingRegister(0x0210, currentSOC))    commError = true;
  uint16_t v;  if (readHoldingRegister(0x020E,v))  currentBatteryVoltage = v*0.01f;  else commError=true;
  uint16_t bp; if (readHoldingRegister(0x020D,bp)) currentBatteryPower   = (int32_t)(int16_t)bp*10; else commError=true;
  uint16_t gp; if (readHoldingRegister(0x0212,gp)) currentGridPower      = (int32_t)(int16_t)gp*10; else commError=true;
  mySocPct = computeMySoc(currentBatteryVoltage, currentBatteryPower);
  if (!commError) lastGoodCommMs = millis();
}

// =============================================================
//  UDP receive — "G<watts>,P<pct>,R<0|1>"
// =============================================================
void receiveGridUDP() {
  int len = udp.parsePacket();
  if (len <= 0) return;
  char buf[48]; int n = udp.read(buf, sizeof(buf)-1);
  if (n <= 0) return;
  buf[n] = '\0';
  if (buf[0] != 'G') return;

  char* p = buf+1;
  udpGridW = (int32_t)atoi(p);

  char* cp = strchr(p, ',');
  if (cp) {
    cp++;
    if (*cp == 'P') {
      udpWattrPct = atof(cp+1);
      char* cr = strchr(cp, ',');
      if (cr && *(cr+1)=='R') udpWattrSaturated = (atoi(cr+2)==1);
    }
  } else {
    udpWattrPct = 0;
    udpWattrSaturated = (udpGridW > 4000 + WATTROUTER_SETPOINT_W);
  }

  lastGridRxMs  = millis();
  gridDataFresh = true;

  // If we were in hold/gone mode, mark that data returned
  if (gridDataHeld) {
    Serial.println("[UDP] Data returned after gap — entering resume wait");
    gridDataHeld   = false;
    waitingForResume = true;
    gridDataGoneMs  = millis();  // reuse timer as resume-start
  }
}

// =============================================================
//  autoRegulate — called every AUTO_REG_INTERVAL_MS
// =============================================================
void autoRegulate() {
  uint32_t now = millis();

  // ---- 1. Manual override window ----
  if (now - lastCmdMs < (uint32_t)MANUAL_OVERRIDE_S*1000) return;

  // ---- 2. UDP data freshness logic ----
  bool dataLive = gridDataFresh && (now - lastGridRxMs) < GRID_HOLD_MS;

  if (!dataLive && gridDataFresh) {
    // Data has gone stale — enter HOLD mode
    if (!gridDataHeld) {
      gridDataHeld   = true;
      gridDataGoneMs = now;
      Serial.printf("[UDP] Data gap — HOLDING current command for %lus\n",
                    (unsigned long)(GRID_HOLD_MS/1000));
    }

    uint32_t goneFor = now - gridDataGoneMs;

    if (goneFor < GRID_HOLD_MS) {
      // Still within hold window — DO NOTHING, keep last command running
      return;
    }

    // Hold window expired — go standby and wait
    if (arState != AR_STANDBY) {
      Serial.println("[UDP] Hold expired — STANDBY until data returns");
      setStandby();
      arState        = AR_STANDBY;
      stateEnteredMs = now;
      confirmCount   = 0;
    }
    return;
  }

  // ---- 3. Post-gap resume settle ----
  if (waitingForResume) {
    if (now - gridDataGoneMs < GRID_RESUME_WAIT_MS) {
      return;  // fresh data arrived but wait for grid to settle
    }
    waitingForResume = false;
    Serial.println("[UDP] Resume settle done — regulation active");
  }

  // ---- 4. Data is live, regulation active ----
  if (!gridDataFresh) return;   // never received any packet yet

  int32_t grid = udpGridW;

  // Compute desired target
  AutoRegState desired = AR_STANDBY;
  uint16_t     targetW = 0;

  if (udpWattrSaturated && grid > WATTROUTER_SETPOINT_W + CHARGE_ENGAGE_SURPLUS_W) {
    desired = AR_CHARGING;
    targetW = (uint16_t)min((int32_t)MAX_WATTS, grid - WATTROUTER_SETPOINT_W);

  } else if (grid < -(int32_t)DISCHARGE_ENGAGE_DEFICIT_W) {
    desired = AR_DISCHARGING;
    targetW = (uint16_t)min((int32_t)MAX_WATTS, -grid - DISCHARGE_DISENGAGE_DEFICIT_W);
    if (targetW < 50) targetW = 50;

  } else {
    // Apply hysteresis — don't switch to standby the moment edge is crossed
    if (arState == AR_CHARGING) {
      // Stay charging until well below threshold
      if (udpWattrSaturated && grid >= WATTROUTER_SETPOINT_W + CHARGE_DISENGAGE_SURPLUS_W) {
        desired = AR_CHARGING;
        targetW = (uint16_t)min((int32_t)MAX_WATTS, grid - WATTROUTER_SETPOINT_W);
      }
    } else if (arState == AR_DISCHARGING) {
      // Stay discharging until well above threshold
      if (grid <= -(int32_t)DISCHARGE_DISENGAGE_DEFICIT_W) {
        desired = AR_DISCHARGING;
        targetW = (uint16_t)min((int32_t)MAX_WATTS, -grid - DISCHARGE_DISENGAGE_DEFICIT_W);
        if (targetW < 50) targetW = 50;
      }
    }
  }

  // ---- 5. Confirm state transition ----
  transitionTo(desired);

  // ---- 6. Execute — only if state matches desired AND within deadband ----
  switch (arState) {
    case AR_STANDBY:
      if (lastSentState != AR_STANDBY) {
        setStandby();
      }
      break;

    case AR_CHARGING:
      if (desired == AR_CHARGING) {
        setChargeAuto(targetW);
      }
      break;

    case AR_DISCHARGING:
      if (desired == AR_DISCHARGING) {
        setDischargeAuto(targetW);
      }
      break;
  }
}

// =============================================================
//  DISPLAY — yellow-band aware, 4 pages
// =============================================================

static void drawYellowHeader(const char* title, const char* pill) {
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(2, 4);
  display.print(title);
  int16_t x1,y1; uint16_t pw,ph;
  display.getTextBounds(pill, 0, 0, &x1, &y1, &pw, &ph);
  display.setCursor(SCREEN_WIDTH - pw - 2, 4);
  display.print(pill);
  display.drawLine(0, YELLOW_H-1, SCREEN_WIDTH-1, YELLOW_H-1, SSD1306_WHITE);
}

static void drawPageDots() {
  for (uint8_t i = 0; i < NUM_PAGES; i++) {
    int x = SCREEN_WIDTH - NUM_PAGES*7 + i*7;
    if (i==currentPage) display.fillCircle(x,62,2,SSD1306_WHITE);
    else                display.drawCircle(x,62,2,SSD1306_WHITE);
  }
}

static int centreX(const char* str, uint8_t sz) {
  display.setTextSize(sz);
  int16_t x1,y1; uint16_t w,h;
  display.getTextBounds(str,0,0,&x1,&y1,&w,&h);
  return (SCREEN_WIDTH - (int)w)/2;
}

// ---------------------------------------------------------------
//  Page 0 — POWER
// ---------------------------------------------------------------
void drawPage0_Power() {
  display.clearDisplay();

  // Yellow header: title centre = direction
  const char* dir = isDischarging() ? "DISCHARGING" : isCharging() ? "CHARGING" : "IDLE";
  drawYellowHeader("BATTERY", commError ? "ERR" : "OK");
  // Overwrite centre of yellow with direction
  display.setTextSize(1);
  int16_t x1,y1; uint16_t dw,dh;
  display.getTextBounds(dir,0,0,&x1,&y1,&dw,&dh);
  display.setCursor((SCREEN_WIDTH-dw)/2, 4);
  display.print(dir);

  // Large power number
  char valBuf[12], unitBuf[8];
  formatPowerLarge(currentBatteryPower, valBuf, sizeof(valBuf), unitBuf, sizeof(unitBuf));

  display.setTextSize(3);
  display.getTextBounds(valBuf,0,0,&x1,&y1,&dw,&dh);
  int16_t x1u,y1u; uint16_t uw,uh;
  display.setTextSize(2);
  display.getTextBounds(unitBuf,0,0,&x1u,&y1u,&uw,&uh);

  uint16_t total = dw + 3 + uw;
  int startX = (SCREEN_WIDTH - total)/2;

  display.setTextSize(3);
  display.setCursor(startX, BLUE_Y+4);
  display.print(valBuf);
  display.setTextSize(2);
  display.setCursor(startX+dw+3, BLUE_Y+10);
  display.print(unitBuf);

  // Grid + voltage bottom row
  display.setTextSize(1);
  display.setCursor(0, 50);
  display.print("GRID:");
  if (gridDataFresh && (millis()-lastGridRxMs) < GRID_HOLD_MS) {
    char gBuf[12]; formatPower(udpGridW, gBuf, sizeof(gBuf));
    display.print(gBuf);
    if (gridDataHeld) display.print("~");  // tilde = stale but held
  } else {
    display.print("--");
  }
  char vbuf[10]; snprintf(vbuf,sizeof(vbuf),"%.1fV",currentBatteryVoltage);
  display.getTextBounds(vbuf,0,0,&x1,&y1,&dw,&dh);
  display.setCursor(SCREEN_WIDTH-dw, 50);
  display.print(vbuf);

  drawPageDots();
  display.display();
}

// ---------------------------------------------------------------
//  Page 1 — SOC
// ---------------------------------------------------------------
void drawPage1_SOC() {
  display.clearDisplay();

  char invLabel[12]; snprintf(invLabel, sizeof(invLabel), "INV %u%%", (unsigned)min((uint16_t)100,currentSOC));
  drawYellowHeader("STATE OF CHARGE", invLabel);

  char socStr[8]; snprintf(socStr, sizeof(socStr), "%u%%", (unsigned)mySocPct);
  int cx = centreX(socStr, 3);
  display.setTextSize(3); display.setTextColor(SSD1306_WHITE);
  display.setCursor(cx, BLUE_Y+3);
  display.print(socStr);

  const int barX=2, barY=46, barW=SCREEN_WIDTH-4, barH=9;
  display.drawRect(barX, barY, barW, barH, SSD1306_WHITE);
  int fill = (int)((mySocPct * (long)(barW-2)) / 100);
  if (fill > 0) display.fillRect(barX+1, barY+1, fill, barH-2, SSD1306_WHITE);

  display.setTextSize(1);
  display.setCursor(barX+1, barY+barH+1); display.print("0%");
  int16_t x1,y1; uint16_t tw,th;
  display.getTextBounds("100%",0,0,&x1,&y1,&tw,&th);
  display.setCursor(barX+barW-tw, barY+barH+1); display.print("100%");

  drawPageDots();
  display.display();
}

// ---------------------------------------------------------------
//  Page 2 — AUTO-REG STATUS
// ---------------------------------------------------------------
void drawPage2_Status() {
  display.clearDisplay();

  // Pill shows confirm progress if transitioning
  char pill[12];
  if (confirmCount > 0 && pendingState != arState)
    snprintf(pill, sizeof(pill), "->%s %u/%u", arStateName(pendingState), confirmCount, CONFIRM_TICKS);
  else
    snprintf(pill, sizeof(pill), "%s", arStateName(arState));

  drawYellowHeader("AUTO-REG", pill);

  const char* arBig;
  switch (arState) {
    case AR_CHARGING:    arBig = "CHARGING";  break;
    case AR_DISCHARGING: arBig = "DISCHARG."; break;
    default:             arBig = "STANDBY";   break;
  }
  int cx = centreX(arBig, 2);
  display.setTextSize(2); display.setTextColor(SSD1306_WHITE);
  display.setCursor(cx, BLUE_Y+3);
  display.print(arBig);

  // Data status line
  display.setTextSize(1);
  display.setCursor(0, 36);
  if (waitingForResume) {
    uint32_t rem = GRID_RESUME_WAIT_MS - (millis()-gridDataGoneMs);
    display.printf("UDP settle %lus", (unsigned long)(rem/1000+1));
  } else if (gridDataHeld) {
    uint32_t held = millis() - gridDataGoneMs;
    uint32_t rem  = GRID_HOLD_MS > held ? GRID_HOLD_MS - held : 0;
    char hb[28]; snprintf(hb,sizeof(hb),"HOLD %lus/%lus",
      (unsigned long)(held/1000), (unsigned long)(GRID_HOLD_MS/1000));
    display.print(hb);
  } else {
    display.print("INV: "); display.print(stateName(currentState));
    display.print(commError ? " ERR" : " OK");
  }

  // Wattrouter line
  display.setCursor(0, 46);
  display.print("WTTR: ");
  display.print(udpWattrSaturated ? "FULL " : "REG  ");
  char pctBuf[8]; snprintf(pctBuf, sizeof(pctBuf), "%.0f%%", udpWattrPct);
  display.print(pctBuf);

  // Manual override or active
  display.setCursor(0, 56);
  uint32_t now = millis();
  if (now - lastCmdMs < (uint32_t)MANUAL_OVERRIDE_S*1000) {
    uint32_t rem = MANUAL_OVERRIDE_S - (now-lastCmdMs)/1000;
    char mb[24]; snprintf(mb,sizeof(mb),"MANUAL %lus left",(unsigned long)rem);
    display.print(mb);
  } else {
    display.print("AUTO-REG ACTIVE");
  }

  drawPageDots();
  display.display();
}

// ---------------------------------------------------------------
//  Page 3 — SYSTEM
// ---------------------------------------------------------------
void drawPage3_System() {
  display.clearDisplay();

  uint32_t now = millis();
  char upBuf[16]; formatDurationShort(now, upBuf, sizeof(upBuf));
  drawYellowHeader("SYSTEM", upBuf);

  uint32_t commAge = (lastGoodCommMs==0) ? 0xFFFFFFFFu : (now-lastGoodCommMs);
  char caBuf[16]; formatDurationShort(commAge, caBuf, sizeof(caBuf));

  display.setTextSize(1);
  display.setCursor(0, BLUE_Y+2); display.print("Last Modbus OK:");
  display.setTextSize(2);
  int cx = centreX(caBuf, 2);
  display.setCursor(cx, BLUE_Y+12); display.print(caBuf);

  uint32_t gridAge = gridDataFresh ? (now-lastGridRxMs) : 0xFFFFFFFFu;
  char gaBuf[16]; formatDurationShort(gridAge, gaBuf, sizeof(gaBuf));
  display.setTextSize(1);
  display.setCursor(0, 40);
  display.print("UDP age: "); display.print(gaBuf);
  if (gridDataHeld)        display.print(" HOLD");
  else if (waitingForResume) display.print(" WAIT");

  display.setCursor(0, 50);
  char statBuf[32];
  snprintf(statBuf, sizeof(statBuf), "HB %u/%u  CMD %u/%u",
    (unsigned)heartbeatsSuccess, (unsigned)heartbeatsSent,
    (unsigned)commandsSuccess,   (unsigned)commandsSent);
  display.print(statBuf);

  drawPageDots();
  display.display();
}

void updateDisplay() {
  uint32_t now = millis();
  if (now - lastPageChange >= PAGE_ROTATE_MS) {
    lastPageChange = now;
    currentPage = (currentPage+1) % NUM_PAGES;
  }
  switch (currentPage) {
    case 0: drawPage0_Power();  break;
    case 1: drawPage1_SOC();    break;
    case 2: drawPage2_Status(); break;
    case 3: drawPage3_System(); break;
  }
}

void displayMessage(const String &l1, const String &l2="", const String &l3="") {
  display.clearDisplay();
  display.setTextSize(1); display.setTextColor(SSD1306_WHITE);
  display.setCursor(0,4);  display.println(l1);
  if (l2.length()) { display.setCursor(0,22); display.println(l2); }
  if (l3.length()) { display.setCursor(0,40); display.println(l3); }
  display.display();
}

// =============================================================
//  Serial interface
// =============================================================
static char    cmdBuf[64];
static uint8_t cmdLen = 0;

static void printHelp() {
  Serial.println("\nCommands:");
  Serial.println("  c <W>   charge (60s manual override)");
  Serial.println("  d <W>   discharge (60s manual override)");
  Serial.println("  s       standby");
  Serial.println("  a       resume auto immediately");
  Serial.println("  p       print status now");
  Serial.println("  h / ?   help\n");
}

static void printStatusLine() {
  uint32_t now = millis();
  uint32_t cAge = (lastGoodCommMs==0)?0xFFFFFFFFu:(now-lastGoodCommMs);
  uint32_t gAge = gridDataFresh?(now-lastGridRxMs):0xFFFFFFFFu;
  char cBuf[12], gBuf[12];
  formatDurationShort(cAge, cBuf, sizeof(cBuf));
  formatDurationShort(gAge, gBuf, sizeof(gBuf));
  Serial.printf("[%lus] AR:%s(%s) Inv:%s SOC:%u%%/%u%% Bat:%ldW Grid:%ldW "
                "Sat:%s Pct:%.0f%% Hold:%s CommOK:%s UDPage:%s\n",
    now/1000,
    arStateName(arState),
    (confirmCount>0&&pendingState!=arState) ? arStateName(pendingState) : "=",
    stateName(currentState),
    (unsigned)mySocPct, (unsigned)min((uint16_t)100,currentSOC),
    (long)currentBatteryPower, (long)udpGridW,
    udpWattrSaturated?"YES":"no", udpWattrPct,
    gridDataHeld?"YES":"no",
    cBuf, gBuf);
}

static void handleCommandLine(char* line) {
  while (*line && isspace((unsigned char)*line)) line++;
  if (!*line) return;
  char c = (char)tolower((unsigned char)line[0]);
  uint16_t watts = 0;
  char* p = line+1; while(*p&&isspace((unsigned char)*p)) p++;
  if (*p) { long w=strtol(p,nullptr,10); if(w<0)w=0; if(w>65535)w=65535; watts=(uint16_t)w; }

  bool ok = true;
  switch(c) {
    case 'c':
      if (!watts) { Serial.println("Usage: c <W>"); ok=false; break; }
      if (watts>MAX_WATTS) { Serial.printf("Clamping to %uW\n",MAX_WATTS); watts=MAX_WATTS; }
      displayMessage("CMD: CHARGE", String(watts)+" W");
      ok=setCharge(watts);
      arState=AR_CHARGING; stateEnteredMs=millis(); confirmCount=0;
      break;
    case 'd':
      if (!watts) { Serial.println("Usage: d <W>"); ok=false; break; }
      if (watts>MAX_WATTS) { Serial.printf("Clamping to %uW\n",MAX_WATTS); watts=MAX_WATTS; }
      displayMessage("CMD: DISCHARGE", String(watts)+" W");
      ok=setDischarge(watts);
      arState=AR_DISCHARGING; stateEnteredMs=millis(); confirmCount=0;
      break;
    case 's':
      displayMessage("CMD: STANDBY");
      ok=setStandby();
      arState=AR_STANDBY; stateEnteredMs=millis(); confirmCount=0;
      break;
    case 'a':
      lastCmdMs=0;
      arState=AR_STANDBY; stateEnteredMs=millis(); confirmCount=0;
      setStandby();
      displayMessage("AUTO-REG","Resumed");
      Serial.println("→ Auto resumed"); return;
    case 'p':
      updateReadings(); updateDisplay(); printStatusLine(); return;
    case 'h': case '?':
      printHelp(); return;
    default:
      Serial.printf("Unknown '%c' (h for help)\n",c); ok=false; break;
  }
  delay(350);
  updateReadings(); updateDisplay();
  Serial.println(ok?"OK":"FAILED");
  printStatusLine();
}

static void processSerial() {
  while (Serial.available()) {
    char ch = (char)Serial.read();
    if (ch=='\r') continue;
    if (ch=='\n') { cmdBuf[cmdLen]='\0'; handleCommandLine(cmdBuf); cmdLen=0; continue; }
    if (cmdLen<sizeof(cmdBuf)-1) cmdBuf[cmdLen++]=ch; else cmdLen=0;
  }
}

// =============================================================
//  SETUP
// =============================================================
void setup() {
  Serial.begin(115200);
  delay(200);

  Wire.begin(SDA_PIN, SCL_PIN);
  if (!display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR)) {
    Serial.println("SSD1306 FAIL"); while(1) delay(100);
  }

  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1);
  display.setCursor(2,4); display.print("SOFAR ME3000SP");
  display.drawLine(0,15,127,15,SSD1306_WHITE);
  display.setTextSize(2);
  display.setCursor(10,20); display.print("UDP");
  display.setCursor(10,38); display.print("AUTO-REG");
  display.display();
  delay(1500);

  pinMode(RTS_PIN, OUTPUT); digitalWrite(RTS_PIN, LOW);
  Serial2.begin(BAUD, SERIAL_8N1, RX_PIN, TX_PIN);

  displayMessage("Connecting WiFi...", WIFI_SSID);
  WiFi.mode(WIFI_STA); WiFi.begin(WIFI_SSID, WIFI_PASS);
  uint32_t wt = millis();
  while (WiFi.status()!=WL_CONNECTED && millis()-wt<15000) { delay(300); Serial.print('.'); }
  if (WiFi.status()==WL_CONNECTED) {
    Serial.printf("\nWiFi OK: %s\n", WiFi.localIP().toString().c_str());
    displayMessage("WiFi OK", WiFi.localIP().toString());
  } else {
    Serial.println("\nWiFi FAIL");
    displayMessage("WiFi FAIL","Auto-reg disabled");
  }
  delay(700);

  udp.begin(UDP_PORT);
  Serial.printf("UDP port %u\n", UDP_PORT);

  displayMessage("RS485 test...");
  uint16_t st;
  if (readHoldingRegister(0x0200, st)) {
    lastGoodCommMs=millis(); displayMessage("RS485 OK","Connected");
    Serial.println("✓ Modbus OK"); delay(700);
  } else {
    displayMessage("RS485 FAIL","Check wiring");
    Serial.println("✗ Modbus FAIL"); delay(2500);
  }

  uint16_t wm;
  if (readHoldingRegister(0x1110, wm)) {
    const char* modes[]={"Auto","ToU","Timing","Passive","Peak"};
    Serial.printf("Work Mode: %s (%u)\n", (wm<=4)?modes[wm]:"Unk", wm);
    if (wm==3) { displayMessage("Passive mode","Ready!"); }
    else       { displayMessage("WARNING:","Not Passive!","Set Menu 12.4"); delay(2500); }
  }

  stateEnteredMs = millis();
  updateReadings();
  lastPageChange = millis();
  updateDisplay();

  Serial.println("\n╔══════════════════════════════════════╗");
  Serial.println("║  Sofar ME3000SP  — Stable Auto-Reg   ║");
  Serial.println("╚══════════════════════════════════════╝");
  printHelp();
}

// =============================================================
//  MAIN LOOP
// =============================================================
void loop() {
  static uint32_t lastHeartbeat=0, lastDisplay=0;
  uint32_t now = millis();

  processSerial();
  receiveGridUDP();

  if (now - lastAutoRegMs >= AUTO_REG_INTERVAL_MS) {
    lastAutoRegMs = now;
    autoRegulate();
  }

  if (now - lastHeartbeat >= 3000) {
    lastHeartbeat = now;
    if (now - lastCmdMs >= 250)
      if (!sendHeartbeat()) Serial.println("✗ Heartbeat timeout");
  }

  if (now - lastDisplay >= 2000) {
    lastDisplay = now;
    updateReadings(); updateDisplay(); printStatusLine();
  }

  delay(10);
}
