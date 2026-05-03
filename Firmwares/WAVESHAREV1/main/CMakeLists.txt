idf_component_register(
    SRCS "test_esp_lcd_st7703.c"
    REQUIRES esp_wifi esp_netif esp_event nvs_flash esp_http_client esp-tls json esp_timer esp_wifi_remote esp_hosted
)
