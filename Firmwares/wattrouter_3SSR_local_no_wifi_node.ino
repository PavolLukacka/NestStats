#include <ModbusMaster.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <SoftwareSerial.h>

// Use SoftwareSerial for Modbus communication
#define RTS_PIN D3
#define RX_PIN D4   // ESP8266 RX pin
#define TX_PIN D7   // ESP8266 TX pin

// OLED Display configuration
#define OLED_SDA   14            // GPIO14 (D5 on NodeMCU)
#define OLED_SCL   12            // GPIO12 (D6 on NodeMCU)
#define OLED_ADDR  0x3C
#define OLED_W     128
#define OLED_H     64
#define OLED_RST   -1

// Relays - only 3 relays now
#define RELAY3 D8 
#define RELAY2 D1  
#define RELAY1 D0 

#define THERMOSTAT  D2 

// Timing constants for RELAY1 modulation
const unsigned long cycleDuration = 20;  // Duration of one cycle in milliseconds at 50Hz
const float totalCycles = 4.0;           // Total cycles for both ON and OFF states for full power

// State tracking
bool relay1State = false;                     // Current state of RELAY1
unsigned long lastToggleTime = 0;             // Last time RELAY1 state was toggled
unsigned long lastGridCheckTime = 0;          // Last time the grid state was checked
unsigned long lastDisplayUpdate = 0;          // Last display update time
unsigned long onDuration = 0;                 // Duration RELAY1 is ON
unsigned long offDuration = 0;                // Duration RELAY1 is OFF
unsigned long currentDuration = 0;            // Current duration to wait before toggling RELAY1
float powerPercentage = 0;                    // Current power percentage for RELAY1
const unsigned long gridCheckInterval = 200;  // Interval to check grid state in milliseconds
const unsigned long displayUpdateInterval = 500; // Update display every 500ms
int gridState = 0;
bool gridFetch = false;

// Regulation statistics
float totalEnergyRegulated = 0;  // kWh
unsigned long systemUptime = 0;
float averageGridState = 0;
float maxGridState = -99999;
float minGridState = 99999;
unsigned long sampleCount = 0;

// Display pages
enum DisplayPage {
  PAGE_MAIN = 0,
  PAGE_RELAYS,
  PAGE_COUNT
};
DisplayPage currentPage = PAGE_MAIN;
unsigned long lastPageChange = 0;
const unsigned long pageDisplayTime = 3000; // 3 seconds per page

bool relay2On = false;  // Current state of RELAY2
bool relay3On = false;  // Current state of RELAY3

// Create SoftwareSerial object for Modbus
SoftwareSerial modbusSerial(RX_PIN, TX_PIN);
ModbusMaster node;

// OLED Display object
Adafruit_SSD1306 display(OLED_W, OLED_H, &Wire, OLED_RST);

void preTransmission() {
  digitalWrite(RTS_PIN, HIGH);
  delay(1); // Small delay to ensure RTS is stable
}

void postTransmission() {
  delay(1); // Small delay before releasing RTS
  digitalWrite(RTS_PIN, LOW);
}

bool FetchSM_PtGridData(int &gridState) {
  const int maxRetries = 9;  // Maximum number of retries
  int retryCount = 0;
  
  Serial.print("Attempting to fetch grid data... ");
  
  while (retryCount < maxRetries) {
    delay(50);
    uint8_t result = node.readHoldingRegisters(0x0488, 1);
    
    Serial.print("Retry ");
    Serial.print(retryCount + 1);
    Serial.print(": Result = 0x");
    Serial.print(result, HEX);

    if (result == node.ku8MBSuccess) {
      int16_t pccPower = node.getResponseBuffer(0);
      float pccPowerInKW = pccPower * 0.01;  // Grid state in kW
      gridState = pccPower * 10;             // Grid state in W
      Serial.print(" SUCCESS - Raw: ");
      Serial.print(pccPower);
      Serial.print(", Grid State: ");
      Serial.print(gridState);
      Serial.println("W");
      
      // Update statistics
      sampleCount++;
      averageGridState = ((averageGridState * (sampleCount - 1)) + gridState) / sampleCount;
      if (gridState > maxGridState) maxGridState = gridState;
      if (gridState < minGridState) minGridState = gridState;
      
      // Update energy regulation (approximate)
      totalEnergyRegulated += (powerPercentage / 100.0) * 2.0 * (gridCheckInterval / 3600000.0); // Assume 2kW max load
      
      return true;
    } else {
      // Print specific error codes
      Serial.print(" FAILED - Error: ");
      switch(result) {
        case 0x01: Serial.print("ILLEGAL_FUNCTION"); break;
        case 0x02: Serial.print("ILLEGAL_DATA_ADDRESS"); break;
        case 0x03: Serial.print("ILLEGAL_DATA_VALUE"); break;
        case 0x04: Serial.print("SLAVE_DEVICE_FAILURE"); break;
        case 0xE0: Serial.print("INVALID_SLAVE_ID"); break;
        case 0xE1: Serial.print("INVALID_FUNCTION"); break;
        case 0xE2: Serial.print("RESPONSE_TIMEOUT"); break;
        case 0xE3: Serial.print("INVALID_CRC"); break;
        default: Serial.print("UNKNOWN_ERROR"); break;
      }
      Serial.println();
    }
    
    retryCount++;
    delay(100);  // Delay between retries
  }
  
  Serial.println("All retries failed!");
  return false;  // Return false if all retries failed
}

String formatTime(unsigned long milliseconds) {
  unsigned long seconds = milliseconds / 1000;
  unsigned long minutes = seconds / 60;
  unsigned long hours = minutes / 60;
  
  return String(hours) + "h " + String(minutes % 60) + "m";
}

String formatPower(int watts) {
  if (abs(watts) >= 1000) {
    float kW = watts / 1000.0;
    return String(kW, 1) + "kW";
  }
  return String(watts) + "W";
}

void updateOLEDDisplay() {
  unsigned long currentTime = millis();
  
  // Auto-rotate pages every few seconds
  if (currentTime - lastPageChange >= pageDisplayTime) {
    currentPage = (DisplayPage)((currentPage + 1) % PAGE_COUNT);
    lastPageChange = currentTime;
  }
  
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  
  switch (currentPage) {
    case PAGE_MAIN:
    {
      // Main page - Grid state and regulation info
      display.setTextSize(1);
      display.setCursor(0, 0);
      display.println("GRID REGULATION");
      display.drawLine(0, 10, 128, 10, SSD1306_WHITE);
      
      // Grid state
      display.setTextSize(2);
      display.setCursor(0, 15);
      if (gridState >= 0) {
        display.print("+");
      }
      display.println(formatPower(gridState));
      
      // Power percentage bar
      display.setTextSize(1);
      display.setCursor(0, 35);
      display.print("PWR: ");
      display.print(powerPercentage, 0);
      display.println("%");
      
      // Draw power bar
      int barWidth = (int)(powerPercentage * 1.0); // 100px max width
      display.drawRect(0, 45, 102, 8, SSD1306_WHITE);
      display.fillRect(1, 46, barWidth, 6, SSD1306_WHITE);
      
      // Thermostat status
      display.setCursor(0, 56);
      int thermostatState = digitalRead(THERMOSTAT);
      display.print("Rezim: ");
      display.println(thermostatState == HIGH ? "SIET" : "PREBYTKY");
      break;
    }
      
    case PAGE_RELAYS:
    {
      // Relay status page
      display.setTextSize(1);
      display.setCursor(0, 0);
      display.println("RELAY STATUS");
      display.drawLine(0, 10, 128, 10, SSD1306_WHITE);
      
      display.setCursor(0, 20);
      display.print("R1: ");
      display.print(powerPercentage, 0);
      display.println("%");
      
      display.setCursor(0, 35);
      display.print("R2: ");
      display.println(relay2On ? "ON" : "OFF");
      
      display.setCursor(0, 50);
      display.print("R3: ");
      display.println(relay3On ? "ON" : "OFF");
      
      // Active relays count
      int activeRelays = (relay2On ? 1 : 0) + (relay3On ? 1 : 0) + (powerPercentage > 0 ? 1 : 0);
      display.setCursor(80, 35);
      display.print("Active: ");
      display.print(activeRelays);
      display.println("/3");
      break;
    }
  }
  
  // Page indicator dots
  for (int i = 0; i < PAGE_COUNT; i++) {
    int x = 110 + (i * 6);
    if (i == currentPage) {
      display.fillCircle(x, 60, 2, SSD1306_WHITE);
    } else {
      display.drawCircle(x, 60, 2, SSD1306_WHITE);
    }
  }
  
  display.display();
}

void updateDurations() {
  float onCycles = (powerPercentage / 100.0) * totalCycles;
  onDuration = onCycles * cycleDuration;
  offDuration = (totalCycles - onCycles) * cycleDuration;
}

void setup() {
  pinMode(THERMOSTAT, INPUT_PULLUP);
  
  // Initialize Serial FIRST and add startup delay
  Serial.begin(115200);
  delay(2000); // Wait for serial monitor to connect
  Serial.println();
  Serial.println("=== SYSTEM STARTUP ===");
  Serial.println("Initializing ESP8266 Grid Regulation System...");

  // Initialize OLED display
  Wire.begin(OLED_SDA, OLED_SCL);
  if (!display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR)) {
    Serial.println(F("SSD1306 allocation failed"));
    while (true) { delay(1); }
  }
  Serial.println("OLED Display initialized successfully");
  
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  display.println("Initializing...");
  display.display();

  pinMode(RTS_PIN, OUTPUT);
  digitalWrite(RTS_PIN, LOW);
  Serial.println("RTS pin configured");

  // Initialize relay pins (only 3 relays)
  pinMode(RELAY1, OUTPUT);
  digitalWrite(RELAY1, LOW);
  pinMode(RELAY2, OUTPUT);
  digitalWrite(RELAY2, LOW);
  pinMode(RELAY3, OUTPUT);
  digitalWrite(RELAY3, LOW);
  Serial.println("Relay pins configured");

  // Initialize Modbus with SoftwareSerial
  modbusSerial.begin(9600);
  node.begin(0x01, modbusSerial);  // Use SoftwareSerial instead of Serial
  node.preTransmission(preTransmission);
  node.postTransmission(postTransmission);
  Serial.println("Modbus initialized with SoftwareSerial");

  Serial.println("System initialized - Starting relay regulation with 3 relays");
  
  // Show startup complete
  display.clearDisplay();
  display.setCursor(0, 0);
  display.println("System je pripraveny!");
  display.println("Regulacia spustena");
  display.display();
  delay(2000);
  
  Serial.println("=== SYSTEM READY ===");
  Serial.println("Starting main loop...");
}

void loop() {
  static unsigned long belowThresholdStartTime = 0;
  unsigned long currentTime = millis();

  // Print heartbeat every 5 seconds
  static unsigned long lastHeartbeat = 0;
  if (currentTime - lastHeartbeat >= 5000) {
    Serial.print("Heartbeat - Uptime: ");
    Serial.print(formatTime(currentTime));
    Serial.print(", Grid Fetch: ");
    Serial.println(gridFetch ? "OK" : "FAILED");
    lastHeartbeat = currentTime;
  }

  // Fetch grid data periodically
  if (currentTime - lastGridCheckTime >= gridCheckInterval) {
    Serial.println("--- Grid Check Cycle ---");
    
    if (FetchSM_PtGridData(gridState)) {r
      gridFetch = true;
      Serial.print("Grid State: ");
      Serial.print(gridState);
      Serial.println(" Watts");

      // Check thermostat state - REVERSED LOGIC
      int thermostatState = digitalRead(THERMOSTAT);
      
      if (thermostatState == LOW) {  // Thermostat is HIGH - Normal regulation mode
        Serial.println("Thermostat LOW - Normal regulation mode (400W)");
        
        // PID controller for efficient regulation
        static float previousError = 0;
        static float integral = 0;
        float setPoint = 400;  // Desired regulation point
        float error = setPoint - gridState;
        integral += error * gridCheckInterval * 0.001;
        float derivative = (error - previousError) / gridCheckInterval;
        previousError = error;

        // PID constants
        float Kp = 0.02;
        float Ki = 0.015;
        float Kd = 0;

        float pidOutput = Kp * error + Ki * integral + Kd * derivative;

        // Dynamic step adjustment based on error magnitude
        float maxStepSize = 1;
        float errorMagnitude = fabs(error);
        float dynamicStep = (errorMagnitude < 1500) ? (maxStepSize * (errorMagnitude / 7000.0)) : (maxStepSize * (errorMagnitude / 1500.0));
        dynamicStep = constrain(dynamicStep, 0.05, maxStepSize);

        // Scale factor for output adjustment
        float scaleFactor = min(1 + fabs(pidOutput) / 15000, 10.0f);

        // Multiplier adjustment for gridState below +100W threshold
        if (gridState < 100) {
          if (belowThresholdStartTime == 0) {
            belowThresholdStartTime = currentTime;
          }
          unsigned long belowThresholdDuration = (currentTime - belowThresholdStartTime) / 500;
          float multiplier = 1.0 + (belowThresholdDuration * 0.5);
          scaleFactor *= multiplier;
        } else {
          belowThresholdStartTime = 0;
        }

        float deadband = 150;

        if (fabs(error) > deadband) {
          if (gridState > 400) {
            powerPercentage += dynamicStep * scaleFactor;
            if (powerPercentage > 100) {
              // Handle relay cascading (only 3 relays now)
              if (!relay2On && !relay3On) {
                relay2On = true;
                digitalWrite(RELAY2, HIGH);
                powerPercentage = 0;
                Serial.println("RELAY2 activated, RELAY1 reset to 0%");
              } else if (relay2On && !relay3On) {
                relay3On = true;
                digitalWrite(RELAY3, HIGH);
                powerPercentage = 0;
                Serial.println("RELAY3 activated, RELAY1 reset to 0%");
              } else if (relay2On && relay3On) {
                powerPercentage = 100;
                Serial.println("All relays at maximum");
              }
            }
          } else {
            powerPercentage -= dynamicStep * scaleFactor;
            if (powerPercentage < 0) {
              powerPercentage = 0;
              if (relay3On) {
                relay3On = false;
                digitalWrite(RELAY3, LOW);
                powerPercentage = 100;
                Serial.println("RELAY3 deactivated, RELAY1 set to 100%");
              } else if (relay2On) {
                relay2On = false;
                digitalWrite(RELAY2, LOW);
                powerPercentage = 100;
                Serial.println("RELAY2 deactivated, RELAY1 set to 100%");
              }
            }
          }
        }
        
        powerPercentage = constrain(powerPercentage, 0, 100);
        
      } else {  // Thermostat is LOW - All relays to 100%
        Serial.println("Thermostat LOW - All relays activated to 100%");
        
        // Turn on all relays
        if (!relay2On) {
          relay2On = true;
          digitalWrite(RELAY2, HIGH);
          Serial.println("RELAY2 activated (thermostat mode)");
        }
        if (!relay3On) {
          relay3On = true;
          digitalWrite(RELAY3, HIGH);
          Serial.println("RELAY3 activated (thermostat mode)");
        }
        
        powerPercentage = 100;
      }
      
      updateDurations();
      Serial.print("Power Percentage (RELAY1): ");
      Serial.print(powerPercentage);
      Serial.print("%, ON duration: ");
      Serial.print(onDuration);
      Serial.print("ms, OFF duration: ");
      Serial.print(offDuration);
      Serial.println("ms");
      
    } else {
      Serial.println("CRITICAL: Failed to fetch grid state after all retries!");
      gridFetch = false;

      // Read thermostat (INPUT_PULLUP: LOW = call for heat)
      int thermostatState = digitalRead(THERMOSTAT);
      if (thermostatState == LOW) {
        // Thermostat calling: turn ALL relays ON (1, 2 and 3)
        relay1State = true;
        digitalWrite(RELAY1, HIGH);

        if (!relay2On) {
          relay2On = true;
          digitalWrite(RELAY2, HIGH);
        }
        if (!relay3On) {
          relay3On = true;
          digitalWrite(RELAY3, HIGH);
        }

        powerPercentage = 100;
        Serial.println("THERMOSTAT FALLBACK: ALL RELAYS ON");

      } else {
        // Thermostat idle: turn ALL relays OFF
        relay1State = false;
        digitalWrite(RELAY1, LOW);

        if (relay2On) {
          relay2On = false;
          digitalWrite(RELAY2, LOW);
        }
        if (relay3On) {
          relay3On = false;
          digitalWrite(RELAY3, LOW);
        }

        powerPercentage = 0;
        Serial.println("THERMOSTAT FALLBACK: ALL RELAYS OFF");
      }

      // Reset PWM timer so it can't fire accidentally later
      lastToggleTime = currentTime;
      Serial.println("--- End Grid Check (fallback) ---");
    }
  }
  
  // Update OLED display periodically
  if (currentTime - lastDisplayUpdate >= displayUpdateInterval) {
    updateOLEDDisplay();
    lastDisplayUpdate = currentTime;
  }
  
  // Toggle RELAY1 based on PWM timing
  if (gridFetch) {
    if (powerPercentage >= 100.0f) {
      // full on: never PWM
      relay1State = true;
      digitalWrite(RELAY1, HIGH);
    }
    else if (powerPercentage <= 0.0f) {
      // full off: never PWM
      relay1State = false;
      digitalWrite(RELAY1, LOW);
    }
    else {
      // 1–99 %: PWM as before
      if (currentTime - lastToggleTime >= currentDuration) {
        relay1State = !relay1State;
        digitalWrite(RELAY1, relay1State ? HIGH : LOW);
        currentDuration = relay1State ? onDuration : offDuration;
        lastToggleTime = currentTime;
      }
    }
  }
}
