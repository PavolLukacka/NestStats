#include <SoftwareSerial.h>
#include <ESP8266WiFi.h>
#include <ESP32_Supabase.h>
#include <ArduinoJson.h>
#include <ModbusMaster.h>

//NODE MCU params
#define RTS_PIN D1
#define RX_PIN D2
#define TX_PIN D3

//Wifi params
#define _SSID "CHANGE_ME_WIFI_SSID"
#define _PASSWORD "CHANGE_ME_WIFI_PASSWORD"

//SUPABASE params
#define PROJECT_URL "https://CHANGE_ME.supabase.co"
#define API_KEY "CHANGE_ME_SUPABASE_ANON_KEY"

//SOLAR SYSTEM INFO
#define NUMBER_OF_STRINGS 2
#define SN_NUMBER "CHANGE_ME_SYSTEM_SN"
Supabase db;

ModbusMaster node;
SoftwareSerial ss(RX_PIN, TX_PIN);

void preTransmission() {
  digitalWrite(RTS_PIN, HIGH);
}

void postTransmission() {
  digitalWrite(RTS_PIN, LOW);
}

bool FetchPVData() {
  uint8_t result;

  result = node.readHoldingRegisters(0x0584, 50);

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

bool fetchBatteryData() {
  uint8_t result;

  // Read data using function code 0x03 (Read)
  result = node.readHoldingRegisters(0x0604, 32);  // Read 32 registers starting from 0x0604

  if (result == node.ku8MBSuccess) {
    int i = 0;  // Assuming one battery input
    int dataIndex = i * 16;

    // Fetch raw data from Modbus registers
    int rawVoltage = node.getResponseBuffer(dataIndex);
    int rawCurrent = node.getResponseBuffer(dataIndex + 1);
    int rawPower = node.getResponseBuffer(dataIndex + 2);
    int rawTemperature = node.getResponseBuffer(dataIndex + 3);
    int rawSOC = node.getResponseBuffer(dataIndex + 4);
    int rawSOH = node.getResponseBuffer(dataIndex + 5);
    int rawChargeCycle = node.getResponseBuffer(dataIndex + 6);

    // Apply scaling factors and convert to float
    float voltage = static_cast<float>(rawVoltage) / 10.0;
    float current = static_cast<float>(rawCurrent) / 100.0;
    float power = static_cast<float>(rawPower) / 100.0;
    float temperature = static_cast<float>(rawTemperature);
    float soc = static_cast<float>(rawSOC);
    float soh = static_cast<float>(rawSOH);
    float chargeCycle = static_cast<float>(rawChargeCycle);

    // Print the fetched data
    Serial.println("Voltage: " + String(voltage) + " V");
    Serial.println("Current: " + String(current) + " A");
    Serial.println("Power: " + String(power) + " kW");
    Serial.println("Temperature: " + String(temperature));
    Serial.println("SOC: " + String(soc));
    Serial.println("SOH: " + String(soh));
    Serial.println("Charge Cycle: " + String(chargeCycle));

    // Upload data to Supabase
    DynamicJsonDocument doc(200);
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

    int code = db.insert("BATTERY_INFORMATION", jsonData, false);  // Assuming "BATTERY_INFORMATION" is your table name
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

bool StatisticalData() {
  uint8_t result;

  // Read data using function code 0x03 (Read)
  result = node.readHoldingRegisters(0x0680, 32);  // Read 32 registers starting from 0x0680

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
    int code = db.insert("STATISTICAL_INFORMATION", jsonData, false); // Change "STATISTICAL_INFORMATION" to your Supabase table name
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

void setup() {
  pinMode(RTS_PIN, OUTPUT);
  digitalWrite(RTS_PIN, LOW);

  Serial.begin(115200);
  db.begin(PROJECT_URL, API_KEY);
  ss.begin(9600);
  node.begin(0x01, ss);
  node.preTransmission(preTransmission);
  node.postTransmission(postTransmission);

  WiFi.begin(_SSID, _PASSWORD);

  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }

  Serial.println("");
  Serial.println("WiFi Connected");
  Serial.print("IP Address: ");
  Serial.println(WiFi.localIP());
}

void loop() {
  if (FetchPVData()) {
    Serial.println("PV Data fetched and uploaded successfully to Firebase.");
  } else {
    Serial.println("Unable to fetch PV data from the inverter.");
  }

  if (fetchBatteryData()) {
    Serial.println("Battery Data fetched and uploaded successfully to Firebase.");
  } else {
    Serial.println("Unable to fetch battery data from the inverter.");
  }

  if (StatisticalData()) {
    Serial.println("Statistical data fetched and uploaded successfully to Firebase.");
  } else {
    Serial.println("Unable to fetch statistical data from the inverter.");
  }
  delay(120000); // delay pre zopakovanie zaslania 2 minúty
}
