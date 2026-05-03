#include <ESP32_Supabase.h>
#include <ArduinoJson.h>
#include <ModbusMaster.h>

// Use hardware serial for Modbus communication
#define RTS_PIN 5
#define RX_PIN 17
#define TX_PIN 16

// Relays
#define RELAY3 15
#define RELAY2 2
#define RELAY1 4

//LEDS
#define LED1 13
#define LED2 12
#define LED3 33

//HEAT OVERRIDE
#define THERMOSTAT 26

// WiFi parameters
#define _SSID "CHANGE_ME_WIFI_SSID"
#define _PASSWORD "CHANGE_ME_WIFI_PASSWORD"

//SUPABASE parameters
#define PROJECT_URL "https://CHANGE_ME.supabase.co"
#define API_KEY "CHANGE_ME_SUPABASE_ANON_KEY"

// Timing constants for RELAY1 modulation
const unsigned long cycleDuration = 20;  // Duration of one cycle in milliseconds at 50Hz
const float totalCycles = 4.0;           // Total cycles for both ON and OFF states for full power

// State tracking
bool relay1State = false;                     // Current state of RELAY1
unsigned long lastToggleTime = 0;             // Last time RELAY1 state was toggled
unsigned long lastGridCheckTime = 0;          // Last time the grid state was checked
unsigned long onDuration = 0;                 // Duration RELAY1 is ON
unsigned long offDuration = 0;                // Duration RELAY1 is OFF
unsigned long currentDuration = 0;            // Current duration to wait before toggling RELAY1
float powerPercentage = 0;                    // Current power percentage for RELAY1
const unsigned long gridCheckInterval = 200;  // Interval to check grid state in milliseconds
int gridState = 0;
bool gridFetch = false;

bool relay2On = false;  // Current state of RELAY2
bool relay3On = false;  // Current state of RELAY3

//SOLAR SYSTEM INFO
#define NUMBER_OF_STRINGS 2
#define SN_NUMBER "CHANGE_ME_SYSTEM_SN"
Supabase db;

ModbusMaster node;
SemaphoreHandle_t modbusMutex;  // Semaphore handle for Modbus mutex

void preTransmission() {
  digitalWrite(RTS_PIN, HIGH);
}

void postTransmission() {
  digitalWrite(RTS_PIN, LOW);
}

bool FetchSM_PtGridData(int &gridState) {
  const int maxRetries = 9;  // Maximum number of retries
  int retryCount = 0;
  while (retryCount < maxRetries) {
    if (xSemaphoreTake(modbusMutex, portMAX_DELAY) == pdTRUE) {
      delay(50);  // Consider reducing this delay if possible
      uint8_t result = node.readHoldingRegisters(0x0488, 1);
      xSemaphoreGive(modbusMutex);

      if (result == node.ku8MBSuccess) {
        int16_t pccPower = node.getResponseBuffer(0);
        float pccPowerInKW = pccPower * 0.01;  // Grid state in kW
        gridState = pccPower * 10;             // Grid state in W
        return true;
      }
      retryCount++;
      delay(100);  // Delay between retries
    }
  }
  return false;  // Return false if all retries failed
}
bool fetchPVData() {

  if (xSemaphoreTake(modbusMutex, portMAX_DELAY) == pdTRUE) {
    uint8_t result = node.readHoldingRegisters(0x0584, 50);
    xSemaphoreGive(modbusMutex);  // Release the mutex

    if (result == node.ku8MBSuccess) {
      DynamicJsonDocument doc(200);
      // Add SN_NUMBER to the JSON document
      doc["sn_number"] = SN_NUMBER;

      for (int i = 0; i < NUMBER_OF_STRINGS; ++i) {
        int voltageIndex = i * 3;
        int currentIndex = voltageIndex + 1;
        int powerIndex = voltageIndex + 2;

        float voltage = round((float)node.getResponseBuffer(voltageIndex) / 10 * 100) / 100;
        float current = round((float)node.getResponseBuffer(currentIndex) / 100 * 100) / 100;
        float power = round((float)node.getResponseBuffer(powerIndex) / 100 * 100) / 100;

        doc["mppt"] = i + 1;
        doc["voltage"] = voltage;
        doc["current"] = current;
        doc["power"] = power;

        Serial.println("MPPT " + String(i + 1) + " - Voltage: " + String(voltage) + " V");
        Serial.println("MPPT " + String(i + 1) + " - Current: " + String(current) + " A");
        Serial.println("MPPT " + String(i + 1) + " - Power: " + String(power) + " kW");

        // Upload data to Supabase
        String jsonData;
        serializeJson(doc, jsonData);

        Serial.print("Serialized JSON Data: ");
        Serial.println(jsonData);

        int code = db.insert("PV_INFORMATION", jsonData, false);
        Serial.print("Supabase Insert Code: ");
        Serial.println(code);

        // Check if the code is not equal to 201
        if (code != 201) {
          Serial.println("Supabase Insert Code is not 201. Upload failed.");
          return false;
        }
      }
      return true;
    }
    return false;
  }
  return false;
}

bool fetchStatisticalData() {
  if (xSemaphoreTake(modbusMutex, portMAX_DELAY) == pdTRUE) {
    uint8_t result = node.readHoldingRegisters(0x0680, 32);  // Read 32 registers starting from 0x0680
    xSemaphoreGive(modbusMutex);                             // Release the mutex

    if (result == node.ku8MBSuccess) {
      // Define an array to store the fetched data
      uint32_t fetchedData[16];

      for (int i = 0; i < 16; ++i) {
        int dataIndex = i * 2;
        fetchedData[i] = (node.getResponseBuffer(dataIndex) << 16) | node.getResponseBuffer(dataIndex + 1);
      }

      // Display the fetched data for each field on the screen
      Serial.println("PV Gen. Today: " + String((float)fetchedData[2] / 100.0) + " kWh");
      Serial.println("PV Gen. Total: " + String((float)fetchedData[3] / 10.0) + " kWh");
      Serial.println("Cons.Today: " + String((float)fetchedData[4] / 100.0) + " kWh");
      Serial.println("Cons. Total: " + String((float)fetchedData[5] / 10.0) + " kWh");
      Serial.println("Purch. Today: " + String((float)fetchedData[6] / 100.0) + " kWh");
      Serial.println("Purch. Total: " + String((float)fetchedData[7] / 10.0) + " kWh");
      Serial.println("Sell Today: " + String((float)fetchedData[8] / 100.0) + " kWh");
      Serial.println("Sell Total: " + String((float)fetchedData[9] / 10.0) + " kWh");
      Serial.println("bCharge Today: " + String((float)fetchedData[10] / 100.0) + " kWh");
      Serial.println("bCharge Total: " + String((float)fetchedData[11] / 10.0) + " kWh");
      Serial.println("bDisch. Today: " + String((float)fetchedData[12] / 100.0) + " kWh");
      Serial.println("bDisch. Total: " + String((float)fetchedData[13] / 10.0) + " kWh");

      // Upload data to Supabase for statistical information
      DynamicJsonDocument doc(200);
      doc["sn_number"] = SN_NUMBER;
      doc["pv_generation_today"] = (float)fetchedData[2] / 100.0;
      doc["pv_generation_total"] = (float)fetchedData[3] / 10.0;
      doc["consumption_today"] = (float)fetchedData[4] / 100.0;
      doc["consumption_total"] = (float)fetchedData[5] / 10.0;
      doc["purchase_today"] = (float)fetchedData[6] / 100.0;
      doc["purchase_total"] = (float)fetchedData[7] / 10.0;
      doc["sell_today"] = (float)fetchedData[8] / 100.0;
      doc["sell_total"] = (float)fetchedData[9] / 10.0;
      doc["battery_charge_today"] = (float)fetchedData[10] / 100.0;
      doc["battery_charge_total"] = (float)fetchedData[11] / 10.0;
      doc["battery_discharge_today"] = (float)fetchedData[12] / 100.0;
      doc["battery_discharge_total"] = (float)fetchedData[13] / 10.0;

      // Convert JSON document to string
      String jsonData;
      serializeJson(doc, jsonData);

      Serial.print("Serialized JSON Data: ");
      Serial.println(jsonData);

      // Upload data to Supabase
      int code = db.insert("STATISTICAL_INFORMATION", jsonData, false);  // Change "STATISTICAL_INFORMATION" to your Supabase table name
      Serial.print("Supabase Insert Code: ");
      Serial.println(code);

      // Check if the code is not equal to 201
      if (code != 201) {
        Serial.println("Supabase Insert Code is not 201. Upload failed.");
        return false;
      }

      return true;
    }
    return false;
  }
  return false;
}

bool fetchBatteryData() {
  if (xSemaphoreTake(modbusMutex, portMAX_DELAY) == pdTRUE) {
    uint8_t result = node.readHoldingRegisters(0x0604, 32);
    xSemaphoreGive(modbusMutex);  // Release the mutex

    if (result == node.ku8MBSuccess) {
      DynamicJsonDocument doc(200);
      int dataIndex = 0;  // Assuming one battery input, and correct offsets

      float voltage = static_cast<float>(node.getResponseBuffer(dataIndex)) / 10.0;
      // Assuming current and power are int16_t and need conversion similar to the second function
      float current = static_cast<float>(int16_t(node.getResponseBuffer(dataIndex + 1))) * 0.01;
      float power = static_cast<float>(int16_t(node.getResponseBuffer(dataIndex + 2))) * 0.01;
      float temperature = static_cast<float>(node.getResponseBuffer(dataIndex + 3));
      float soc = static_cast<float>(node.getResponseBuffer(dataIndex + 4));
      float soh = static_cast<float>(node.getResponseBuffer(dataIndex + 5));
      float chargeCycle = static_cast<float>(node.getResponseBuffer(dataIndex + 6));

      doc["sn_number"] = SN_NUMBER;
      doc["voltage"] = voltage;
      doc["current"] = current;
      doc["power"] = power;
      doc["temperature"] = temperature;
      doc["soc"] = soc;
      doc["soh"] = soh;
      doc["charge_cycle"] = chargeCycle;

      String jsonData;
      serializeJson(doc, jsonData);
      Serial.print("Serialized JSON Data: ");
      Serial.println(jsonData);

      int code = db.insert("BATTERY_INFORMATION", jsonData, false);
      Serial.print("Supabase Insert Code: ");
      Serial.println(code);

      if (code != 201) {
        Serial.println("Supabase Insert Code is not 201. Upload failed.");
        return false;
      }
      delay(200);

      return true;
    }
    return false;
  }
  return false;
}

bool fetchGridData() {
  if (xSemaphoreTake(modbusMutex, portMAX_DELAY) == pdTRUE) {
    // Read a block of registers starting at 0x0480 to 0x04BD
    uint8_t result = node.readHoldingRegisters(0x0480, 62);
    xSemaphoreGive(modbusMutex);  // Release the mutex

    if (result == node.ku8MBSuccess) {
      DynamicJsonDocument doc(4096);  // Sufficient size for all data fields

      // Helper function to parse and scale values
      auto parseRegister = [](uint16_t index, float scale) -> float {
        return static_cast<float>(int16_t(node.getResponseBuffer(index))) * scale;
      };
      // Parse grid connection and real-time data
      doc["sn_number"] = SN_NUMBER;
      doc["grid_frequency"] = parseRegister(4, 0.01);             // 0x0484
      doc["active_power_output_total"] = parseRegister(5, 0.01);  // 0x0485
      doc["active_power_pcc_total"] = parseRegister(8, 0.01);     // 0x0488
      doc["voltage_phase_r"] = parseRegister(13, 0.1);            // 0x048D
      doc["voltage_phase_s"] = parseRegister(24, 0.1);            // 0x0498
      doc["voltage_phase_t"] = parseRegister(35, 0.1);            // 0x04A3
      doc["current_output_r"] = parseRegister(14, 0.01);          // 0x048E
      doc["current_output_s"] = parseRegister(25, 0.01);          // 0x0499
      doc["current_output_t"] = parseRegister(36, 0.01);          // 0x04A4
      doc["active_power_output_r"] = parseRegister(15, 0.01);     // 0x048F
      doc["active_power_output_s"] = parseRegister(26, 0.01);     // 0x049A
      doc["active_power_output_t"] = parseRegister(37, 0.01);     // 0x04A5
      doc["current_pcc_r"] = parseRegister(18, 0.01);             // 0x0492
      doc["current_pcc_s"] = parseRegister(29, 0.01);             // 0x049D
      doc["current_pcc_t"] = parseRegister(40, 0.01);             // 0x04A8
      doc["active_power_pcc_r"] = parseRegister(19, 0.01);        // 0x0493
      doc["active_power_pcc_s"] = parseRegister(30, 0.01);        // 0x049E
      doc["active_power_pcc_t"] = parseRegister(41, 0.01);        // 0x04A9

      // Serialize JSON to send
      String jsonData;
      serializeJson(doc, jsonData);
      Serial.print("Serialized JSON Data: ");
      Serial.println(jsonData);

      // Simulate inserting data into a database
      int code = db.insert("GRID_INFORMATION", jsonData, false);
      Serial.print("Database Insert Code: ");
      Serial.println(code);

      if (code != 201) {
        Serial.println("Database Insert Code is not 201. Upload failed.");
        return false;
      }
      delay(200);
      return true;
    }
    return false;
  }
  return false;
}

bool fetchWattrouterInfo() {
  DynamicJsonDocument doc(4096);  // Sufficient size for all data fields

  doc["sn_number"] = SN_NUMBER;
  doc["gridFetch"] = gridFetch;
  doc["powerPercentage"] = powerPercentage;
  doc["relay2On"] = relay2On;
  doc["relay3On"] = relay3On;

  // Serialize JSON to send
  String jsonData;
  serializeJson(doc, jsonData);
  Serial.print("Serialized JSON Data: ");
  Serial.println(jsonData);

  // Simulate inserting data into a database
  int code = db.insert("WATTROUTER_INFO", jsonData, false);
  Serial.print("Database Insert Code: ");
  Serial.println(code);

  if (code != 201) {
    Serial.println("Database Insert Code is not 201. Upload failed.");
    return false;
  }
  delay(200);
  return true;
}


void updateDurations() {
  float onCycles = (powerPercentage / 100.0) * totalCycles;
  onDuration = onCycles * cycleDuration;
  offDuration = (totalCycles - onCycles) * cycleDuration;
}

void setup() {

  pinMode(LED1, OUTPUT);
  digitalWrite(LED1, LOW);  // Ensure LED1 starts in the OFF state

  pinMode(LED2, OUTPUT);
  digitalWrite(LED2, LOW);  // Ensure LED2 starts in the OFF state

  pinMode(LED3, OUTPUT);
  digitalWrite(LED3, LOW);  // Ensure LED3 starts in the OFF state

  Serial.begin(115200);
  modbusMutex = xSemaphoreCreateMutex();  // Create the mutex
  if (modbusMutex == NULL) {
    Serial.println("Failed to create Modbus mutex");
  }

  Serial.println("Starting WiFi connection...");

  // Initialize WiFi
  WiFi.begin(_SSID, _PASSWORD);
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
    digitalWrite(LED2, HIGH); // Indicate wifi conection process 
  }
  Serial.println("\nWiFi connected");
  digitalWrite(LED2, LOW); // Indicate sucesfull wifi conection process 
  Serial.print("IP address: ");
  Serial.println(WiFi.localIP());

  pinMode(THERMOSTAT, INPUT_PULLUP);
  pinMode(RTS_PIN, OUTPUT);
  digitalWrite(RTS_PIN, LOW);

  pinMode(RELAY1, OUTPUT);
  digitalWrite(RELAY1, LOW);  // Ensure RELAY1 starts in the OFF state

  pinMode(RELAY2, OUTPUT);
  digitalWrite(RELAY2, LOW);  // Ensure RELAY2 starts in the OFF state

  pinMode(RELAY3, OUTPUT);
  digitalWrite(RELAY3, LOW);  // Ensure RELAY3 starts in the OFF state

  Serial.begin(115200);
  db.begin(PROJECT_URL, API_KEY);                   // USB Serial for debugging
  Serial2.begin(9600, SERIAL_8N1, RX_PIN, TX_PIN);  // Use Serial2 for Modbus
  node.begin(0x01, Serial2);                        // Use Serial2 for Modbus communication
  node.preTransmission(preTransmission);
  node.postTransmission(postTransmission);

  // Create tasks
  xTaskCreatePinnedToCore(
    adjustRelays,    // Task function
    "AdjustRelays",  // Name of task
    10000,           // Stack size of task
    NULL,            // Parameter of the task
    1,               // Priority of the task
    NULL,            // Task handle
    1                // Core where the task should run
  );

  xTaskCreatePinnedToCore(
    sendDataToSupabase,  // Task function
    "SendData",          // Name of task
    10000,               // Stack size of task
    NULL,                // Parameter of the task
    1,                   // Priority of the task
    NULL,                // Task handle
    0                    // Core where the task should run
  );

  xTaskCreatePinnedToCore(
    fetchGridData,  // Task function
    "GridData",     // Name of task
    10000,          // Stack size of task
    NULL,           // Parameter of the task
    1,              // Priority of the task
    NULL,           // Task handle
    0               // Core where the task should run
  );
}

void loop() {
  // Intentionally left empty
}

void adjustRelays(void *parameter) {
  for (;;) {
    unsigned long currentTime = millis();           // Get the current time
    int thermostatState = digitalRead(THERMOSTAT);  // Read the thermostat state

    if (currentTime - lastGridCheckTime >= gridCheckInterval) {
      if (gridFetch == true) {
        digitalWrite(LED3, LOW);
        Serial.print("Grid State: ");
        Serial.print(gridState);
        Serial.println(" Watts");

        // Adjust steps by 80% for gridState between -150 and +700 watts for greater accuracy
        float adjustmentFactor = 1.0;  // Default adjustment factor
        if (gridState > 70 && gridState < 700) {
          adjustmentFactor = 0.1;  // Reduce steps by 90%
        } else if (gridState > 701 && gridState < 1500) {
          adjustmentFactor = 0.5;  // Reduce steps by 50%
        }

        float distanceFromLowerLimit = fabs(gridState - 200);
        float scaleFactor = min(1 + distanceFromLowerLimit / 500, 10.0f);  // Ensure 10.0 is a float
        scaleFactor *= adjustmentFactor;                                   // Apply adjustment factor

        if (gridState > 100) {                   // Excess power available
          powerPercentage += 0.5 * scaleFactor;  // Adjust the increment by the dynamic scaleFactor
          if (powerPercentage > 100) {
            // Handle the scenario where powerPercentage exceeds 100%
            if (!relay2On && !relay3On) {
              relay2On = true;
              digitalWrite(RELAY2, HIGH);
              powerPercentage = 0;
            } else if (relay2On && !relay3On) {
              relay3On = true;
              digitalWrite(RELAY3, HIGH);
              powerPercentage = 0;
            } else if (relay2On && relay3On) {
              powerPercentage = 100;  // Cap at 100%
            }
          }
        } else {
          // No excess power or a power deficit
          powerPercentage -= 0.5 * scaleFactor;  // Decrease the power percentage with adjusted scaleFactor
          if (powerPercentage < 0) {
            powerPercentage = 0;  // Ensure it doesn't go below 0
            if (relay3On) {
              relay3On = false;
              digitalWrite(RELAY3, LOW);
              powerPercentage = 100;  // Reset to 100% for smooth transition
            } else if (relay2On) {
              relay2On = false;
              digitalWrite(RELAY2, LOW);
              powerPercentage = 100;  // Reset to 100% for smooth transition
            }
          }
        }
        // Thermostat demand check
        if (thermostatState == LOW) {
          Serial.println("Thermostat is demanding heat.");
          powerPercentage = 100;  // Set power to maximum if thermostat demands heat
          digitalWrite(RELAY2, HIGH);
          digitalWrite(RELAY3, HIGH);
          relay2On = true;
          relay3On = true;
        }
        // Ensure powerPercentage stays within 0 to 100%
        powerPercentage = constrain(powerPercentage, 0, 100);
        // Your existing logic for updating durations and handling relay toggling follows here...
        updateDurations();
        Serial.print("Power Percentage (RELAY1): ");
        Serial.println(powerPercentage);
        Serial.print("RELAY2 State: ");
        Serial.println(relay2On ? "ON" : "OFF");
        Serial.print("RELAY3 State: ");
        Serial.println(relay3On ? "ON" : "OFF");
      } else {
        Serial.println("Failed to fetch grid state.");
        powerPercentage = 0;  // Set powerPercentage to 0 to turn off the relay
        // Ensure the relay and the associated LED are turned off
        relay1State = true;

        // Check if the thermostat is demanding heat
        if (thermostatState == LOW) {
          Serial.println("Thermostat is demanding heat.");
          digitalWrite(RELAY1, HIGH);
          digitalWrite(RELAY2, HIGH);
          digitalWrite(RELAY3, HIGH);
          digitalWrite(LED1, HIGH);
          digitalWrite(LED3, HIGH);

        } else {
          Serial.println("Thermostat is not demanding heat, turing off the relay.");
          digitalWrite(RELAY1, LOW);
          digitalWrite(RELAY2, LOW);
          digitalWrite(RELAY3, LOW);
          digitalWrite(LED1, LOW);
          digitalWrite(LED3, HIGH);
          relay2On = false;
          relay3On = false;
        }
      }
      lastGridCheckTime = currentTime;
    }
    // Toggling the state of RELAY1 based on the current duration
    if (currentTime - lastToggleTime >= currentDuration && gridFetch == true) {
      relay1State = !relay1State;
      digitalWrite(RELAY1, relay1State ? HIGH : LOW);
      digitalWrite(LED1, relay1State ? HIGH : LOW);
      currentDuration = relay1State ? onDuration : offDuration;
      lastToggleTime = currentTime;
    }
  }
}

void sendDataToSupabase(void *parameter) {
  for (;;) {
    if (fetchBatteryData()) {
      Serial.println("Battery Data fetched and uploaded successfully to Supabase.");
      digitalWrite(LED2, LOW); 
    } else {
      Serial.println("Unable to Upload Battery Data to Supabase");
      digitalWrite(LED2, HIGH);
    }
    vTaskDelay(pdMS_TO_TICKS(30000));  // delay 30s
    if (fetchStatisticalData()) {
      Serial.println("Statistical Data fetched and uploaded successfully to Supabase.");
      digitalWrite(LED2, LOW); 
    } else {
      Serial.println("Unable to Upload StatisticalData to Supabase");
      digitalWrite(LED2, HIGH);
    }
    vTaskDelay(pdMS_TO_TICKS(30000)); // delay 30s
    if (fetchPVData()) {
      Serial.println("PV Data fetched and uploaded successfully to Supabase.");
      digitalWrite(LED2, LOW); 
    } else {
      Serial.println("Unable to Upload PV Data to Supabase");
      digitalWrite(LED2, HIGH);
    }
    vTaskDelay(pdMS_TO_TICKS(30000)); // delay 30s
    if (fetchGridData()) {
      Serial.println("Grid Data fetched and uploaded successfully to Supabase.");
      digitalWrite(LED2, LOW); 
    } else {
      Serial.println("Unable to Upload Grid Data to Supabase");
      digitalWrite(LED2, HIGH);
    }
    vTaskDelay(pdMS_TO_TICKS(30000)); // delay 30s
    if (fetchWattrouterInfo()) {
      Serial.println("Wattrouter Info fetched and uploaded successfully to Supabase.");
      digitalWrite(LED2, LOW); 
    } else {
      Serial.println("Unable to Upload Wattrouter Info to Supabase");
      digitalWrite(LED2, HIGH); 
    }
    vTaskDelay(pdMS_TO_TICKS(30000)); // delay 30s
  }
}

void fetchGridData(void *parameter) {
  for (;;) {
    if (FetchSM_PtGridData(gridState)) {
      if (xSemaphoreTake(modbusMutex, portMAX_DELAY) == pdTRUE) {
        xSemaphoreGive(modbusMutex);
      }
      gridFetch = true;
    } else {
      gridFetch = false;
    }
    vTaskDelay(pdMS_TO_TICKS(300));  // Run this every 300 ms
  }
}
