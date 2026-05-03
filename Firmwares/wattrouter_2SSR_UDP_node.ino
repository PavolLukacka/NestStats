#include <Arduino.h>
#include <WiFi.h>
#include <WiFiUDP.h>
#include <ESP32_Supabase.h>
#include <ArduinoJson.h>
#include <ModbusMaster.h>
#include <TM1637Display.h>
#include <math.h>

// ---------------------------------------------------------
//  COMPILE-TIME SETTINGS
// ---------------------------------------------------------
#define TEST_MODE 0   // 1 = simulated grid, 0 = real Modbus

// How many consecutive Modbus failures before safety shutdown.
// At 200 ms poll interval, 10 = 2 seconds of bad reads.
#define MAX_CONSEC_FAILURES 10

// ---------------------------------------------------------
//  WiFi & Supabase
// ---------------------------------------------------------
#define WIFI_SSID   "CHANGE_ME_WIFI_SSID"
#define WIFI_PASS   "CHANGE_ME_WIFI_PASSWORD"
#define PROJECT_URL "https://CHANGE_ME.supabase.co"
#define API_KEY     "CHANGE_ME_SUPABASE_ANON_KEY"

#define SN_NUMBER "CHANGE_ME_SYSTEM_SN"
#define NUMBER_OF_STRINGS 2

// ---------------------------------------------------------
//  UDP BROADCAST
//  Change last octet to 255 for your subnet, e.g. 192.168.1.255
// ---------------------------------------------------------
#define UDP_BROADCAST_IP   "192.168.1.255"
#define UDP_BROADCAST_PORT 4210

WiFiUDP udp;

// ---------------------------------------------------------
//  PIN DEFINITIONS
// ---------------------------------------------------------
#define RTS_PIN    5
#define RX_PIN     17
#define TX_PIN     16

#define RELAY1     4     // modulated SSR  (0-2 kW variable)
#define RELAY2     15    // on/off SSR     (2 kW fixed)

#define LED1       13    // follows RELAY1
#define LED2       12    // follows RELAY2
#define LED_ALERT  33    // red - wifi / inverter alert

#define THERMOSTAT 26    // INPUT_PULLUP, LOW = heat demand

// ---------------------------------------------------------
//  TM1637 DISPLAYS
// ---------------------------------------------------------
#define TM1637_CLK     27
#define TM1637_DIO     14
TM1637Display display(TM1637_CLK, TM1637_DIO);        // grid watts

#define TM1637_SYS_CLK 18
#define TM1637_SYS_DIO 21
TM1637Display systemDisplay(TM1637_SYS_CLK, TM1637_SYS_DIO); // SSR power

// ---------------------------------------------------------
//  OBJECTS
// ---------------------------------------------------------
ModbusMaster node;
Supabase     db;

// ---------------------------------------------------------
//  BURST-FIRING CONSTANTS  (50 Hz mains)
// ---------------------------------------------------------
const unsigned long CYCLE_MS   = 20;    // one half-cycle ~20 ms
const float         TOTAL_CYCS = 4.0f; // on+off budget = 100%

// ---------------------------------------------------------
//  REGULATION PARAMETERS  - tweak here only
// ---------------------------------------------------------
const int   SETPOINT_W      = 400;   // target surplus at meter [W]
const float STEP_BASE       = 0.3f;  // base duty-cycle step per poll [%]
const float R1_HEADROOM_ON  = 95.0f; // R1% that triggers cascade UP
const float R1_HANDOFF_PCT  = 50.0f; // R1% set after cascade UP
const int   CASCADE_UP_HYST = 300;   // extra surplus required to cascade UP [W]
const int   CASCADE_DN_HYST = 200;   // deficit margin for cascade DOWN [W]

// ---------------------------------------------------------
//  TIMING
// ---------------------------------------------------------
const unsigned long GRID_POLL_MS  = 200;    // grid read interval
const unsigned long WIFI_RETRY_MS = 15000;  // WiFi reconnect interval
const unsigned long UPLOAD_MS     = 30000;  // Supabase upload slot interval

// ---------------------------------------------------------
//  STATE - relay / regulation
// ---------------------------------------------------------
bool           relay1State  = false;
bool           relay2On     = false;
float          powerPct     = 0.0f;  // R1 duty cycle 0-100%

unsigned long  lastToggleTime   = 0;
unsigned long  lastGridPollTime = 0;
unsigned long  onDuration       = 0;
unsigned long  offDuration      = 0;
unsigned long  currentDuration  = 0;

// Last known good grid reading
int  lastGridW   = 0;
bool inverterOk  = false;
int  consecFails = 0;

// ---------------------------------------------------------
//  STATE - thermostat
// ---------------------------------------------------------
bool  heatDemand     = false;
bool  savedRelay2On  = false;
float savedPowerPct  = 0.0f;
bool  haveSavedState = false;

// ---------------------------------------------------------
//  STATE - WiFi / Supabase
// ---------------------------------------------------------
bool          wifiOk        = false;
unsigned long lastWifiCheck = 0;

// ---------------------------------------------------------
//  STATE - alert LED
// ---------------------------------------------------------
unsigned long lastAlertBlink = 0;
bool          alertLedState  = false;

// ---------------------------------------------------------
//  STATE - Supabase upload cycling
// ---------------------------------------------------------
unsigned long lastUploadTime = 0;
int           uploadStep     = 0;  // cycles 0..4

// =============================================================
//  MODBUS HELPERS
// =============================================================
void preTransmission()  { digitalWrite(RTS_PIN, HIGH); }
void postTransmission() { digitalWrite(RTS_PIN, LOW);  }

// =============================================================
//  FetchSM_PtGridData
//  Returns true and fills gridW [W] on success.
//  POSITIVE = export/surplus, NEGATIVE = import/deficit.
// =============================================================
bool FetchSM_PtGridData(int &gridW)
{
#if TEST_MODE
  // Alternates every 60 s: +1550 W surplus / -1550 W deficit
  static bool          inited = false;
  static unsigned long t0     = 0;
  if (!inited) { t0 = millis(); inited = true; }
  gridW = (((millis() - t0) / 60000UL) % 2 == 0) ? 1550 : -1550;
  Serial.printf("[TEST] Grid = %d W\n", gridW);
  return true;
#else
  uint8_t res = node.readHoldingRegisters(0x0488, 1);
  if (res == node.ku8MBSuccess) {
    gridW = (int)(int16_t)node.getResponseBuffer(0) * 10; // 0.01 kW -> W
    return true;
  }
  return false;
#endif
}

// =============================================================
//  UDP BROADCAST  - called every successful grid poll
//  Packet format: "G<watts>"  e.g. "G1550" or "G-800"
//  Fires instantly (<1 ms) - no blocking wait for ACK
// =============================================================
void broadcastGridWatts(int gridW) {
  if (!wifiOk) return;
  char buf[32];
  bool saturated = (relay2On && powerPct >= 98.0f);
  snprintf(buf, sizeof(buf), "G%d,P%.0f,R%d", gridW, powerPct, saturated ? 1 : 0);
  udp.beginPacket(UDP_BROADCAST_IP, UDP_BROADCAST_PORT);
  udp.write((uint8_t*)buf, strlen(buf));
  udp.endPacket();
}

// =============================================================
//  DISPLAY HELPERS
// =============================================================
void showGridWatts(int w)
{
  display.showNumberDec(constrain(w, -999, 9999), true);
}

void showSystemPower()
{
  float p1 = heatDemand ? 2000.0f : (2000.0f * powerPct / 100.0f);
  float p2 = relay2On   ? 2000.0f : 0.0f;
  int   t  = (int)(p1 + p2 + 0.5f);
  systemDisplay.showNumberDec(constrain(t, 0, 9999), true);
}

// =============================================================
//  updateDurations - recalculate burst-fire timings for R1
// =============================================================
void updateDurations()
{
  if (powerPct >= 99.0f) {
    onDuration      = GRID_POLL_MS;
    offDuration     = 0;
    currentDuration = GRID_POLL_MS;
    return;
  }
  if (powerPct <= 0.0f) {
    onDuration      = 0;
    offDuration     = GRID_POLL_MS;
    currentDuration = GRID_POLL_MS;
    return;
  }
  float on  = (powerPct / 100.0f) * TOTAL_CYCS;
  float off = TOTAL_CYCS - on;
  onDuration  = max((unsigned long)(on  * CYCLE_MS), (unsigned long)CYCLE_MS);
  offDuration = max((unsigned long)(off * CYCLE_MS), (unsigned long)CYCLE_MS);
  currentDuration = relay1State ? onDuration : offDuration;
}

// =============================================================
//  regulateRelays - called each successful grid poll
// =============================================================
void regulateRelays(int gridW)
{
  // ---- dynamic step scaling ----
  float adj = 1.0f;
  if      (gridW >  150 && gridW <  700) adj = 0.1f;
  else if (gridW >  700 && gridW < 1500) adj = 0.5f;
  else if (gridW > -100 && gridW <  200) adj = 20.0f;

  float dist  = fabsf((float)(gridW - 200));
  float scale = fminf(1.0f + dist / 500.0f, 10.0f) * adj;
  float step  = STEP_BASE * scale;

  if (!relay2On) {
    // ---- R1 only ----
    powerPct += (gridW > SETPOINT_W) ? step : -step;
    powerPct  = constrain(powerPct, 0.0f, 100.0f);

    // Cascade UP: R1 saturated AND large surplus
    if (powerPct >= R1_HEADROOM_ON && gridW > SETPOINT_W + CASCADE_UP_HYST) {
      Serial.println("[CASCADE UP] R2 ON, R1 -> 50%");
      relay2On = true;
      digitalWrite(RELAY2, HIGH);
      digitalWrite(LED2,   HIGH);
      powerPct = R1_HANDOFF_PCT;
    }

  } else {
    // ---- R2 running, R1 fine-tunes ----
    if      (gridW > SETPOINT_W + CASCADE_DN_HYST) powerPct += step;
    else if (gridW < SETPOINT_W - CASCADE_DN_HYST) powerPct -= step;
    powerPct = constrain(powerPct, 0.0f, 100.0f);

    // Cascade DOWN: R1 bottomed AND deficit persists
    if (powerPct <= 1.0f && gridW < SETPOINT_W - CASCADE_DN_HYST) {
      Serial.println("[CASCADE DOWN] R2 OFF, R1 -> 100%");
      relay2On = false;
      digitalWrite(RELAY2, LOW);
      digitalWrite(LED2,   LOW);
      powerPct = 100.0f;
    }
  }

  powerPct = constrain(powerPct, 0.0f, 100.0f);
  updateDurations();

  Serial.printf("Grid=%dW  R1=%.1f%%  R2=%s\n",
                gridW, powerPct, relay2On ? "ON" : "OFF");
}

// =============================================================
//  safetyShutdown - only called after MAX_CONSEC_FAILURES
// =============================================================
void safetyShutdown()
{
  Serial.println("[SAFETY] Too many consecutive Modbus failures - shutting relays.");
  powerPct    = 0.0f;
  relay1State = false;
  relay2On    = false;

  digitalWrite(RELAY1, LOW); digitalWrite(LED1, LOW);
  digitalWrite(RELAY2, LOW); digitalWrite(LED2, LOW);

  currentDuration = GRID_POLL_MS;
  display.clear();
}

// =============================================================
//  WiFi - non-blocking reconnect
// =============================================================
void handleWifi(unsigned long now)
{
  wifiOk = (WiFi.status() == WL_CONNECTED);
  if (!wifiOk && now - lastWifiCheck >= WIFI_RETRY_MS) {
    lastWifiCheck = now;
    Serial.println("[WiFi] Reconnecting...");
    WiFi.disconnect(true);
    WiFi.begin(WIFI_SSID, WIFI_PASS);
  }
}

// =============================================================
//  Alert LED - blink on problem, dark when OK
// =============================================================
void updateAlertLed(unsigned long now)
{
  if (!wifiOk || !inverterOk) {
    if (now - lastAlertBlink >= 500) {
      lastAlertBlink = now;
      alertLedState  = !alertLedState;
      digitalWrite(LED_ALERT, alertLedState);
    }
  } else {
    alertLedState = false;
    digitalWrite(LED_ALERT, LOW);
  }
}

// =============================================================
//  SUPABASE UPLOAD FUNCTIONS
// =============================================================

bool uploadBatteryData()
{
  if (!wifiOk) return false;
  if (node.readHoldingRegisters(0x0604, 8) != node.ku8MBSuccess) return false;

  DynamicJsonDocument doc(256);
  doc["sn_number"]    = SN_NUMBER;
  doc["voltage"]      = (float)node.getResponseBuffer(0) / 10.0f;
  doc["current"]      = (float)(int16_t)node.getResponseBuffer(1) * 0.01f;
  doc["power"]        = (float)(int16_t)node.getResponseBuffer(2) * 0.01f;
  doc["temperature"]  = (float)node.getResponseBuffer(3);
  doc["soc"]          = (float)node.getResponseBuffer(4);
  doc["soh"]          = (float)node.getResponseBuffer(5);
  doc["charge_cycle"] = (float)node.getResponseBuffer(6);

  String json; serializeJson(doc, json);
  int code = db.insert("BATTERY_INFORMATION", json, false);
  Serial.printf("[Supabase] BATTERY_INFORMATION -> %d\n", code);
  return (code == 201);
}

bool uploadStatisticalData()
{
  if (!wifiOk) return false;
  if (node.readHoldingRegisters(0x0680, 32) != node.ku8MBSuccess) return false;

  uint32_t fd[16];
  for (int i = 0; i < 16; i++)
    fd[i] = ((uint32_t)node.getResponseBuffer(i * 2) << 16)
             | node.getResponseBuffer(i * 2 + 1);

  DynamicJsonDocument doc(512);
  doc["sn_number"]               = SN_NUMBER;
  doc["pv_generation_today"]     = fd[2]  / 100.0f;
  doc["pv_generation_total"]     = fd[3]  / 10.0f;
  doc["consumption_today"]       = fd[4]  / 100.0f;
  doc["consumption_total"]       = fd[5]  / 10.0f;
  doc["purchase_today"]          = fd[6]  / 100.0f;
  doc["purchase_total"]          = fd[7]  / 10.0f;
  doc["sell_today"]              = fd[8]  / 100.0f;
  doc["sell_total"]              = fd[9]  / 10.0f;
  doc["battery_charge_today"]    = fd[10] / 100.0f;
  doc["battery_charge_total"]    = fd[11] / 10.0f;
  doc["battery_discharge_today"] = fd[12] / 100.0f;
  doc["battery_discharge_total"] = fd[13] / 10.0f;

  String json; serializeJson(doc, json);
  int code = db.insert("STATISTICAL_INFORMATION", json, false);
  Serial.printf("[Supabase] STATISTICAL_INFORMATION -> %d\n", code);
  return (code == 201);
}

bool uploadPVData()
{
  if (!wifiOk) return false;
  if (node.readHoldingRegisters(0x0584, 50) != node.ku8MBSuccess) return false;

  bool ok = true;
  for (int i = 0; i < NUMBER_OF_STRINGS; i++) {
    int vi = i * 3;
    DynamicJsonDocument doc(256);
    doc["sn_number"] = SN_NUMBER;
    doc["mppt"]      = i + 1;
    doc["voltage"]   = roundf((float)node.getResponseBuffer(vi)     / 10.0f  * 100.f) / 100.f;
    doc["current"]   = roundf((float)node.getResponseBuffer(vi + 1) / 100.0f * 100.f) / 100.f;
    doc["power"]     = roundf((float)node.getResponseBuffer(vi + 2) / 100.0f * 100.f) / 100.f;

    String json; serializeJson(doc, json);
    int code = db.insert("PV_INFORMATION", json, false);
    Serial.printf("[Supabase] PV_INFORMATION MPPT%d -> %d\n", i + 1, code);
    if (code != 201) ok = false;
  }
  return ok;
}

bool uploadGridData()
{
  if (!wifiOk) return false;
  if (node.readHoldingRegisters(0x0480, 62) != node.ku8MBSuccess) return false;

  auto reg = [](int idx, float scale) -> float {
    return (float)(int16_t)node.getResponseBuffer(idx) * scale;
  };

  DynamicJsonDocument doc(1024);
  doc["sn_number"]                 = SN_NUMBER;
  doc["grid_frequency"]            = reg( 4, 0.01f);
  doc["active_power_output_total"] = reg( 5, 0.01f);
  doc["active_power_pcc_total"]    = reg( 8, 0.01f);
  doc["voltage_phase_r"]           = reg(13, 0.1f);
  doc["voltage_phase_s"]           = reg(24, 0.1f);
  doc["voltage_phase_t"]           = reg(35, 0.1f);
  doc["current_output_r"]          = reg(14, 0.01f);
  doc["current_output_s"]          = reg(25, 0.01f);
  doc["current_output_t"]          = reg(36, 0.01f);
  doc["active_power_output_r"]     = reg(15, 0.01f);
  doc["active_power_output_s"]     = reg(26, 0.01f);
  doc["active_power_output_t"]     = reg(37, 0.01f);
  doc["current_pcc_r"]             = reg(18, 0.01f);
  doc["current_pcc_s"]             = reg(29, 0.01f);
  doc["current_pcc_t"]             = reg(40, 0.01f);
  doc["active_power_pcc_r"]        = reg(19, 0.01f);
  doc["active_power_pcc_s"]        = reg(30, 0.01f);
  doc["active_power_pcc_t"]        = reg(41, 0.01f);

  String json; serializeJson(doc, json);
  int code = db.insert("GRID_INFORMATION", json, false);
  Serial.printf("[Supabase] GRID_INFORMATION -> %d\n", code);
  return (code == 201);
}

bool uploadWattrouterInfo()
{
  if (!wifiOk) return false;

  StaticJsonDocument<256> doc;
  doc["sn_number"]       = SN_NUMBER;
  doc["gridFetch"]       = inverterOk;
  doc["powerPercentage"] = powerPct;
  doc["relay2On"]        = relay2On;
  doc["relay3On"] = false;
  doc["relay4On"] = false;
  doc["relay5On"] = false;
  doc["relay6On"] = false;
  doc["relay7On"] = false;
  doc["relay8On"] = false;

  String json; serializeJson(doc, json);
  Serial.print("[WATTROUTER_INFO JSON] ");
  Serial.println(json);

  int code = db.insert("WATTROUTER_INFO", json, false);
  Serial.printf("[Supabase] WATTROUTER_INFO -> %d\n", code);
  return (code == 201);
}

// Dispatcher: one table per call, rotates every UPLOAD_MS
void handleSupabaseUploads(unsigned long now)
{
  if (!wifiOk) return;
  if (now - lastUploadTime < UPLOAD_MS) return;
  lastUploadTime = now;

  bool ok = false;
  switch (uploadStep) {
    case 0: ok = uploadBatteryData();     break;
    case 1: ok = uploadStatisticalData(); break;
    case 2: ok = uploadPVData();          break;
    case 3: ok = uploadGridData();        break;
    case 4: ok = uploadWattrouterInfo();  break;
  }
  Serial.printf("[Upload] step %d -> %s\n", uploadStep, ok ? "OK" : "FAIL");
  uploadStep = (uploadStep + 1) % 5;
}

// =============================================================
//  SETUP
// =============================================================
void setup()
{
  Serial.begin(115200);

  pinMode(THERMOSTAT, INPUT_PULLUP);
  pinMode(RTS_PIN,    OUTPUT); digitalWrite(RTS_PIN,   LOW);
  pinMode(RELAY1,     OUTPUT); digitalWrite(RELAY1,    LOW);
  pinMode(RELAY2,     OUTPUT); digitalWrite(RELAY2,    LOW);
  pinMode(LED1,       OUTPUT); digitalWrite(LED1,      LOW);
  pinMode(LED2,       OUTPUT); digitalWrite(LED2,      LOW);
  pinMode(LED_ALERT,  OUTPUT); digitalWrite(LED_ALERT, LOW);

  display.setBrightness(0x0F, true);
  display.showNumberDec(0, true);
  systemDisplay.setBrightness(0x0F, true);
  systemDisplay.showNumberDec(0, true);

  Serial2.begin(9600, SERIAL_8N1, RX_PIN, TX_PIN);
  node.begin(0x01, Serial2);
  node.preTransmission(preTransmission);
  node.postTransmission(postTransmission);

  // WiFi non-blocking - regulation starts immediately regardless
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  lastWifiCheck = millis();

  // Start UDP socket for broadcasting grid watts
  udp.begin(UDP_BROADCAST_PORT);

  db.begin(PROJECT_URL, API_KEY);

  updateDurations();
  Serial.println("[BOOT] Wattrouter started.");
}

// =============================================================
//  MAIN LOOP
// =============================================================
void loop()
{
  unsigned long now = millis();

  // ----------------------------------------------------------
  //  1. THERMOSTAT - edge detection
  // ----------------------------------------------------------
  bool newHeatDemand = (digitalRead(THERMOSTAT) == LOW);
  static bool lastHeatDemand = false;

  if (newHeatDemand != lastHeatDemand) {
    if (newHeatDemand) {
      Serial.println("[THERM] ON - saving state, disabling R2.");
      savedPowerPct  = powerPct;
      savedRelay2On  = relay2On;
      haveSavedState = true;
      relay2On = false;
      digitalWrite(RELAY2, LOW);
      digitalWrite(LED2,   LOW);
    } else {
      Serial.println("[THERM] OFF - restoring saved state.");
      if (haveSavedState) {
        powerPct = savedPowerPct;
        relay2On = savedRelay2On;
        digitalWrite(RELAY2, relay2On ? HIGH : LOW);
        digitalWrite(LED2,   relay2On ? HIGH : LOW);
        updateDurations();
      }
    }
    lastHeatDemand = newHeatDemand;
  }
  heatDemand = newHeatDemand;

  // ----------------------------------------------------------
  //  2. WiFi & alert LED (never blocks)
  // ----------------------------------------------------------
  handleWifi(now);
  updateAlertLed(now);

  // ----------------------------------------------------------
  //  3. GRID POLL
  // ----------------------------------------------------------
  if (now - lastGridPollTime >= GRID_POLL_MS) {
    lastGridPollTime = now;

    int  freshGridW = 0;
    bool readOk     = FetchSM_PtGridData(freshGridW);

    if (readOk) {
      // ---- successful read - reset failure counter ----
      consecFails = 0;
      inverterOk  = true;
      lastGridW   = freshGridW;

      showGridWatts(freshGridW);

      // ---- BROADCAST to SOFAR controller immediately ----
      broadcastGridWatts(freshGridW);

      if (!heatDemand) {
        regulateRelays(freshGridW);
      } else {
        Serial.println("[THERM] Active - skipping grid regulation.");
      }

    } else {
      // ---- failed read ----
      consecFails++;
      Serial.printf("[MODBUS] Fail %d/%d\n", consecFails, MAX_CONSEC_FAILURES);

      if (consecFails >= MAX_CONSEC_FAILURES) {
        inverterOk = false;
        safetyShutdown();

        // Thermostat still gets heat from grid even without inverter comms
        if (heatDemand) {
          Serial.println("[THERM] No inverter - forcing R1 ON for heat.");
          relay1State = true;
          digitalWrite(RELAY1, HIGH);
          digitalWrite(LED1,   HIGH);
          currentDuration = GRID_POLL_MS;
        }
      }
      // else: keep relays as-is (glitch ride-through)
    }

    showSystemPower();
  }

  // ----------------------------------------------------------
  //  4. RELAY-1 BURST-FIRING  (runs every loop iteration)
  // ----------------------------------------------------------
  if (now - lastToggleTime >= currentDuration) {

    if (heatDemand) {
      // Thermostat mode: R1 permanently ON, no modulation
      relay1State = true;
      digitalWrite(RELAY1, HIGH);
      digitalWrite(LED1,   HIGH);
      currentDuration = GRID_POLL_MS;
      lastToggleTime  = now;

    } else if (inverterOk && powerPct > 0.0f) {

      if (powerPct >= 99.0f) {
        // Full power - no toggling needed
        relay1State = true;
        digitalWrite(RELAY1, HIGH);
        digitalWrite(LED1,   HIGH);
        currentDuration = GRID_POLL_MS;
        lastToggleTime  = now;
      } else {
        // Normal burst-fire modulation
        relay1State = !relay1State;
        digitalWrite(RELAY1, relay1State ? HIGH : LOW);
        digitalWrite(LED1,   relay1State ? HIGH : LOW);
        currentDuration = relay1State ? onDuration : offDuration;
        lastToggleTime  = now;
      }

    } else {
      // powerPct == 0 or inverter not OK
      relay1State = false;
      digitalWrite(RELAY1, LOW);
      digitalWrite(LED1,   LOW);
      currentDuration = GRID_POLL_MS;
      lastToggleTime  = now;
    }
  }

  // ----------------------------------------------------------
  //  5. SUPABASE UPLOADS  (non-blocking, one table per slot)
  // ----------------------------------------------------------
  handleSupabaseUploads(now);
}
