/*
 * SOLAR MONITOR v3.0  —  ESP32-P4 + ST7703 (720×720 round/square DSI)
 *
 * Fixes vs v2:
 *   - CRITICAL BUG FIX: xEventGroupWaitBits xWaitForAllBits was pdTRUE
 *     (waited for CONNECTED AND FAIL simultaneously → always timed out).
 *     Changed to pdFALSE so ANY matching bit unblocks the wait.
 *   - CRITICAL BUG FIX: Initial draw happened before fetch_task could
 *     complete even one HTTP round-trip (5 Supabase calls × ~2 s each).
 *     app_main now does a synchronous blocking fetch before first render.
 *   - fetch_task now signals a semaphore on every successful fetch so the
 *     main loop can react immediately instead of polling on a fixed timer.
 *
 * UI improvements:
 *   - Larger value numerals (scale 9 for SOC, scale 7 for power figures)
 *   - Two-column layout with clear visual hierarchy
 *   - Color-coded progress bars for SOC, grid, PV
 *   - Computed total PV power & estimated home consumption
 *   - Boot splash with step-by-step connection status
 *   - Slovak section labels & state descriptions
 *   - Amber accent lines, panel borders, status footer
 *
 * SPDX-FileCopyrightText: 2024 Espressif Systems (Shanghai) CO LTD
 * SPDX-License-Identifier: Apache-2.0
 */

#include "soc/soc_caps.h"

#if SOC_MIPI_DSI_SUPPORTED

#include <inttypes.h>
#include <ctype.h>
#include <string.h>
#include <stdlib.h>
#include <math.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/event_groups.h"
#include "freertos/semphr.h"
#include "driver/i2c.h"
#include "driver/gpio.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "esp_event.h"

/* ── WiFi Kconfig fallbacks ───────────────────────────────────────────────── */
#ifndef CONFIG_ESP_WIFI_STATIC_RX_BUFFER_NUM
#define CONFIG_ESP_WIFI_STATIC_RX_BUFFER_NUM 10
#endif
#ifndef CONFIG_ESP_WIFI_DYNAMIC_RX_BUFFER_NUM
#define CONFIG_ESP_WIFI_DYNAMIC_RX_BUFFER_NUM 32
#endif
#ifndef CONFIG_ESP_WIFI_TX_BUFFER_TYPE
#define CONFIG_ESP_WIFI_TX_BUFFER_TYPE 1
#endif
#ifndef CONFIG_ESP_WIFI_DYNAMIC_RX_MGMT_BUF
#define CONFIG_ESP_WIFI_DYNAMIC_RX_MGMT_BUF 1
#endif
#ifndef CONFIG_ESP_WIFI_CACHE_TX_BUFFER_NUM
#define CONFIG_ESP_WIFI_CACHE_TX_BUFFER_NUM 0
#endif
#ifndef CONFIG_ESP_WIFI_ESPNOW_MAX_ENCRYPT_NUM
#define CONFIG_ESP_WIFI_ESPNOW_MAX_ENCRYPT_NUM 7
#endif
#ifndef CONFIG_WIFI_RMT_STATIC_RX_BUFFER_NUM
#define CONFIG_WIFI_RMT_STATIC_RX_BUFFER_NUM 10
#endif
#ifndef CONFIG_WIFI_RMT_DYNAMIC_RX_BUFFER_NUM
#define CONFIG_WIFI_RMT_DYNAMIC_RX_BUFFER_NUM 32
#endif
#ifndef CONFIG_WIFI_RMT_TX_BUFFER_TYPE
#define CONFIG_WIFI_RMT_TX_BUFFER_TYPE 1
#endif
#ifndef CONFIG_WIFI_RMT_DYNAMIC_RX_MGMT_BUF
#define CONFIG_WIFI_RMT_DYNAMIC_RX_MGMT_BUF 1
#endif
#ifndef CONFIG_WIFI_RMT_CACHE_TX_BUFFER_NUM
#define CONFIG_WIFI_RMT_CACHE_TX_BUFFER_NUM 32
#endif
#ifndef CONFIG_WIFI_RMT_ESPNOW_MAX_ENCRYPT_NUM
#define CONFIG_WIFI_RMT_ESPNOW_MAX_ENCRYPT_NUM 7
#endif

#include "esp_netif.h"
#include "esp_wifi.h"
#include "esp_lcd_panel_commands.h"
#include "esp_lcd_panel_ops.h"
#include "esp_lcd_panel_io.h"
#include "esp_ldo_regulator.h"
#include "esp_dma_utils.h"
#include "esp_http_client.h"
#include "esp_crt_bundle.h"
#include "nvs_flash.h"
#include "cJSON.h"
#include "esp_lcd_mipi_dsi.h"
#include "esp_lcd_st7703.h"

/* ═══════════════════════════════════════════════════════════════════════════
 *  CONFIGURATION
 * ═══════════════════════════════════════════════════════════════════════════ */

/* Display */
#define LCD_H_RES              720
#define LCD_V_RES              720
#define LCD_BPP                16
#define PIN_LCD_RST            27
#define PIN_BK_LIGHT           26
#define BK_LIGHT_ON_LEVEL      0
#define BK_LIGHT_OFF_LEVEL     (!BK_LIGHT_ON_LEVEL)

#if LCD_BPP == 24
#define MIPI_DPI_PX_FMT  LCD_COLOR_PIXEL_FORMAT_RGB888
#elif LCD_BPP == 18
#define MIPI_DPI_PX_FMT  LCD_COLOR_PIXEL_FORMAT_RGB666
#else
#define MIPI_DPI_PX_FMT  LCD_COLOR_PIXEL_FORMAT_RGB565
#endif

#define MIPI_DSI_PHY_PWR_LDO_CHAN      3
#define MIPI_DSI_PHY_PWR_LDO_MV    2500

/* Network */
#define WIFI_SSID           "CHANGE_ME_WIFI_SSID"
#define WIFI_PASS           "CHANGE_ME_WIFI_PASSWORD"
#define WIFI_MAX_RETRY      10

/* Backend */
#define SUPABASE_URL  "https://CHANGE_ME.supabase.co"
#define SUPABASE_KEY  "CHANGE_ME_SUPABASE_ANON_KEY"
#define SN_NUMBER     "CHANGE_ME_SYSTEM_SN"
#define HTTP_BUF_LEN  4096    /* bigger buffer: Supabase sometimes sends verbose JSON */
#define HTTP_LOG_BODY_MAX 240

/* Refresh interval for background fetch task */
#define FETCH_INTERVAL_MS   15000
#define PAGE_SWITCH_MS      8000
#define HISTORY_LEN         60

/* Touch (GT911) */
#define TOUCH_I2C_PORT      I2C_NUM_0
#define TOUCH_SCL_GPIO      8
#define TOUCH_SDA_GPIO      7
#define TOUCH_I2C_FREQ_HZ   400000
#define TOUCH_ADDR1         0x14
#define TOUCH_ADDR2         0x5D
#define TOUCH_I2C_TIMEOUT_MS 50
#define TOUCH_SWAP_XY       0
#define TOUCH_INVERT_X      0
#define TOUCH_INVERT_Y      0

/* ═══════════════════════════════════════════════════════════════════════════
 *  COLOUR PALETTE  (RGB8 constants)
 * ═══════════════════════════════════════════════════════════════════════════ */

/* Dark navy background */
#define C_BG_R   0x04
#define C_BG_G   0x07
#define C_BG_B   0x11

/* Deep-blue header/footer */
#define C_HDR_R  0x08
#define C_HDR_G  0x18
#define C_HDR_B  0x42

/* Panel card background */
#define C_CARD_R 0x07
#define C_CARD_G 0x0F
#define C_CARD_B 0x22

/* Subtle divider/border */
#define C_DIV_R  0x16
#define C_DIV_G  0x26
#define C_DIV_B  0x4A

/* Amber accent */
#define C_AMB_R  0xFF
#define C_AMB_G  0x8C
#define C_AMB_B  0x00

/* Dim text */
#define C_DIM_R  0x44
#define C_DIM_G  0x55
#define C_DIM_B  0x77

/* Mid text */
#define C_MID_R  0x77
#define C_MID_G  0x88
#define C_MID_B  0xAA

/* Bright text */
#define C_BRT_R  0xCC
#define C_BRT_G  0xDD
#define C_BRT_B  0xFF

/* Green (good / charging) */
#define C_GRN_R  0x00
#define C_GRN_G  0xFF
#define C_GRN_B  0x66

/* Cyan (grid) */
#define C_CYN_R  0x00
#define C_CYN_G  0xEE
#define C_CYN_B  0xFF

/* Yellow (solar) */
#define C_YLW_R  0xFF
#define C_YLW_G  0xE0
#define C_YLW_B  0x00

/* Red (alert) */
#define C_RED_R  0xFF
#define C_RED_G  0x22
#define C_RED_B  0x22

/* ═══════════════════════════════════════════════════════════════════════════
 *  DATA MODEL
 * ═══════════════════════════════════════════════════════════════════════════ */
typedef struct {
    float bat_v;          /* Battery voltage  [V]  */
    float bat_i;          /* Battery current  [A]  (+ charge, − discharge) */
    float bat_soc;        /* State of charge  [%]  */
    float grid_pcc_kw;    /* Grid PCC power   [kW] (+ import, − export) */
    float pv1_kw;         /* MPPT string 1    [kW] */
    float pv2_kw;         /* MPPT string 2    [kW] */
    int64_t fetch_us;     /* esp_timer_get_time() at last successful fetch */
} solar_data_t;

typedef struct {
    float pv_total[HISTORY_LEN];
    float pv1[HISTORY_LEN];
    float pv2[HISTORY_LEN];
    float grid[HISTORY_LEN];
    float bat_kw[HISTORY_LEN];
    float spotreba[HISTORY_LEN];
    float soc[HISTORY_LEN];
    int head;
    int count;
} history_t;

typedef enum {
    PAGE_DASH = 0,
    PAGE_BAT,
    PAGE_GRID,
    PAGE_PV,
    PAGE_STATS,
    PAGE_COUNT
} page_t;

/* ═══════════════════════════════════════════════════════════════════════════
 *  GLOBALS
 * ═══════════════════════════════════════════════════════════════════════════ */
static const char *TAG = "solar_monitor";

static esp_ldo_channel_handle_t  ldo_phy     = NULL;
static esp_lcd_panel_handle_t    panel       = NULL;
static esp_lcd_dsi_bus_handle_t  dsi_bus     = NULL;
static esp_lcd_panel_io_handle_t dbi_io      = NULL;
static SemaphoreHandle_t         vsync_sem   = NULL;

static EventGroupHandle_t  wifi_eg           = NULL;
static const int           WIFI_CONN_BIT     = BIT0;
static const int           WIFI_FAIL_BIT     = BIT1;
static uint8_t             wifi_fail_reason  = 0;
static int                 wifi_retry        = 0;

static solar_data_t        g_data            = {0};
static SemaphoreHandle_t   g_data_mutex      = NULL;
/* Posted by fetch_task after every successful fetch so main loop can react */
static SemaphoreHandle_t   g_data_ready_sem  = NULL;

static history_t           g_hist            = {0};
static bool                g_touch_ok        = false;
static uint8_t             g_touch_addr      = 0;
static bool                g_touch_ready     = false;
static int                 g_page_index      = 0;
static bool                g_auto_rotate     = true;
static int64_t             g_last_page_us    = 0;

static const char *PAGE_NAMES[PAGE_COUNT] = {
    "DASH",
    "BAT",
    "GRID",
    "PV",
    "STAT",
};

/* ═══════════════════════════════════════════════════════════════════════════
 *  HELPERS / ASSERTS
 * ═══════════════════════════════════════════════════════════════════════════ */
#define CHK(x)           ESP_ERROR_CHECK(x)
#define ASSERT_NN(x)     assert((x) != NULL)
/* Forward declarations */
static void history_push(history_t *hist, const solar_data_t *d);
static void draw_text(int x, int y, const char *text, uint8_t scale,
                      uint8_t fr, uint8_t fg, uint8_t fb,
                      uint8_t br, uint8_t bg, uint8_t bb);
static void draw_text_c(int bx, int bw, int y, const char *text, uint8_t scale,
                        uint8_t fr, uint8_t fg, uint8_t fb,
                        uint8_t br, uint8_t bg, uint8_t bb);

/* ═══════════════════════════════════════════════════════════════════════════
 *  LCD INITIALISATION
 * ═══════════════════════════════════════════════════════════════════════════ */
IRAM_ATTR static bool on_vsync(esp_lcd_panel_handle_t h,
                               esp_lcd_dpi_panel_event_data_t *e, void *ctx)
{
    BaseType_t hp = pdFALSE;
    xSemaphoreGiveFromISR((SemaphoreHandle_t)ctx, &hp);
    return hp == pdTRUE;
}

static void lcd_init(void)
{
#if PIN_BK_LIGHT >= 0
    gpio_config_t bk = { .mode = GPIO_MODE_OUTPUT,
                         .pin_bit_mask = 1ULL << PIN_BK_LIGHT };
    CHK(gpio_config(&bk));
    CHK(gpio_set_level(PIN_BK_LIGHT, BK_LIGHT_ON_LEVEL));
#endif

#ifdef MIPI_DSI_PHY_PWR_LDO_CHAN
    esp_ldo_channel_config_t ldo_cfg = { .chan_id    = MIPI_DSI_PHY_PWR_LDO_CHAN,
                                         .voltage_mv = MIPI_DSI_PHY_PWR_LDO_MV };
    CHK(esp_ldo_acquire_channel(&ldo_cfg, &ldo_phy));
#endif

    esp_lcd_dsi_bus_config_t bus_cfg = ST7703_PANEL_BUS_DSI_2CH_CONFIG();
    CHK(esp_lcd_new_dsi_bus(&bus_cfg, &dsi_bus));

    esp_lcd_dbi_io_config_t dbi_cfg = ST7703_PANEL_IO_DBI_CONFIG();
    CHK(esp_lcd_new_panel_io_dbi(dsi_bus, &dbi_cfg, &dbi_io));

    esp_lcd_dpi_panel_config_t dpi_cfg = ST7703_720_720_PANEL_60HZ_DPI_CONFIG(MIPI_DPI_PX_FMT);
    st7703_vendor_config_t vendor = {
        .flags = { .use_mipi_interface = 1 },
        .mipi_config = { .dsi_bus = dsi_bus, .dpi_config = &dpi_cfg },
    };
    const esp_lcd_panel_dev_config_t pcfg = {
        .reset_gpio_num = PIN_LCD_RST,
        .rgb_ele_order  = LCD_RGB_ELEMENT_ORDER_RGB,
        .bits_per_pixel = LCD_BPP,
        .vendor_config  = &vendor,
    };
    CHK(esp_lcd_new_panel_st7703(dbi_io, &pcfg, &panel));
    CHK(esp_lcd_panel_reset(panel));
    CHK(esp_lcd_panel_init(panel));

#if LCD_BPP == 16
    uint8_t colmod = 0x55;
    CHK(esp_lcd_panel_io_tx_param(dbi_io, LCD_CMD_COLMOD, &colmod, 1));
#endif
    CHK(esp_lcd_panel_disp_on_off(panel, true));

    vsync_sem = xSemaphoreCreateBinary();
    ASSERT_NN(vsync_sem);
    esp_lcd_dpi_panel_event_callbacks_t cbs = { .on_color_trans_done = on_vsync };
    CHK(esp_lcd_dpi_panel_register_event_callbacks(panel, &cbs, vsync_sem));
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  WiFi
 * ═══════════════════════════════════════════════════════════════════════════ */
static void wifi_event_handler(void *arg, esp_event_base_t base,
                               int32_t id, void *data)
{
    if (base == WIFI_EVENT && id == WIFI_EVENT_STA_START) {
        esp_wifi_connect();
    } else if (base == WIFI_EVENT && id == WIFI_EVENT_STA_DISCONNECTED) {
        wifi_event_sta_disconnected_t *d = data;
        if (d) wifi_fail_reason = d->reason;
        xEventGroupClearBits(wifi_eg, WIFI_CONN_BIT);
        if (wifi_retry < WIFI_MAX_RETRY) { esp_wifi_connect(); wifi_retry++; }
        else xEventGroupSetBits(wifi_eg, WIFI_FAIL_BIT);
    } else if (base == IP_EVENT && id == IP_EVENT_STA_GOT_IP) {
        wifi_retry = 0;
        wifi_fail_reason = 0;
        xEventGroupSetBits(wifi_eg, WIFI_CONN_BIT);
    }
}

static void wifi_init_sta(void)
{
    esp_err_t err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        CHK(nvs_flash_erase());
        err = nvs_flash_init();
    }
    CHK(err);
    CHK(esp_netif_init());
    CHK(esp_event_loop_create_default());
    esp_netif_create_default_wifi_sta();

    wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
    CHK(esp_wifi_init(&cfg));

    wifi_eg = xEventGroupCreate();
    CHK(esp_event_handler_instance_register(WIFI_EVENT, ESP_EVENT_ANY_ID, wifi_event_handler, NULL, NULL));
    CHK(esp_event_handler_instance_register(IP_EVENT, IP_EVENT_STA_GOT_IP, wifi_event_handler, NULL, NULL));

    wifi_config_t wcfg = {0};
    strncpy((char *)wcfg.sta.ssid,     WIFI_SSID, sizeof(wcfg.sta.ssid)     - 1);
    strncpy((char *)wcfg.sta.password, WIFI_PASS, sizeof(wcfg.sta.password) - 1);
    wcfg.sta.threshold.authmode = WIFI_AUTH_WPA2_PSK;
    wcfg.sta.pmf_cfg.capable    = true;

    CHK(esp_wifi_set_mode(WIFI_MODE_STA));
    CHK(esp_wifi_set_config(WIFI_IF_STA, &wcfg));
    CHK(esp_wifi_start());
}

/* ── Wait for connection (or failure). Returns true if connected. ─────────── */
/* BUG FIX: was pdTRUE (wait ALL bits) — now pdFALSE (wait ANY bit).          */
static bool wifi_wait_connected(uint32_t timeout_ms)
{
    if (!wifi_eg) return false;
    EventBits_t b = xEventGroupWaitBits(
        wifi_eg,
        WIFI_CONN_BIT | WIFI_FAIL_BIT,
        pdFALSE,   /* ← FIX: do NOT clear bits after returning         */
        pdFALSE,   /* ← FIX: wait for ANY of the bits, not ALL of them */
        pdMS_TO_TICKS(timeout_ms));
    return (b & WIFI_CONN_BIT) != 0;
}

static bool wifi_is_connected(void)
{
    if (!wifi_eg) return false;
    return (xEventGroupGetBits(wifi_eg) & WIFI_CONN_BIT) != 0;
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  TOUCH (GT911)
 * ═══════════════════════════════════════════════════════════════════════════ */
static esp_err_t touch_i2c_write(uint16_t reg, const uint8_t *data, size_t len)
{
    uint8_t buf[2 + 8];
    if (len > 8) return ESP_FAIL;
    buf[0] = (uint8_t)(reg & 0xFF);
    buf[1] = (uint8_t)((reg >> 8) & 0xFF);
    if (data && len > 0) {
        memcpy(&buf[2], data, len);
    }
    i2c_cmd_handle_t cmd = i2c_cmd_link_create();
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (g_touch_addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_write(cmd, buf, 2 + len, true);
    i2c_master_stop(cmd);
    esp_err_t err = i2c_master_cmd_begin(TOUCH_I2C_PORT, cmd, pdMS_TO_TICKS(TOUCH_I2C_TIMEOUT_MS));
    i2c_cmd_link_delete(cmd);
    return err;
}

static esp_err_t touch_i2c_read(uint16_t reg, uint8_t *data, size_t len)
{
    uint8_t addr[2] = { (uint8_t)(reg & 0xFF), (uint8_t)((reg >> 8) & 0xFF) };
    i2c_cmd_handle_t cmd = i2c_cmd_link_create();
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (g_touch_addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_write(cmd, addr, 2, true);
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (g_touch_addr << 1) | I2C_MASTER_READ, true);
    if (len > 1) {
        i2c_master_read(cmd, data, len - 1, I2C_MASTER_ACK);
    }
    i2c_master_read_byte(cmd, data + len - 1, I2C_MASTER_NACK);
    i2c_master_stop(cmd);
    esp_err_t err = i2c_master_cmd_begin(TOUCH_I2C_PORT, cmd, pdMS_TO_TICKS(TOUCH_I2C_TIMEOUT_MS));
    i2c_cmd_link_delete(cmd);
    return err;
}

static esp_err_t touch_i2c_probe(uint8_t addr)
{
    i2c_cmd_handle_t cmd = i2c_cmd_link_create();
    i2c_master_start(cmd);
    i2c_master_write_byte(cmd, (addr << 1) | I2C_MASTER_WRITE, true);
    i2c_master_stop(cmd);
    esp_err_t err = i2c_master_cmd_begin(TOUCH_I2C_PORT, cmd, pdMS_TO_TICKS(TOUCH_I2C_TIMEOUT_MS));
    i2c_cmd_link_delete(cmd);
    return err;
}

static void touch_i2c_scan(void)
{
    int found = 0;
    for (uint8_t addr = 0x08; addr <= 0x77; addr++) {
        if (touch_i2c_probe(addr) == ESP_OK) {
            ESP_LOGI(TAG, "touch: I2C device @0x%02X", addr);
            found++;
        }
    }
    if (found == 0) {
        ESP_LOGW(TAG, "touch: no I2C devices found on SDA=%d SCL=%d", TOUCH_SDA_GPIO, TOUCH_SCL_GPIO);
    }
}
static void touch_init(void)
{
    i2c_config_t cfg = {
        .mode = I2C_MODE_MASTER,
        .sda_io_num = TOUCH_SDA_GPIO,
        .scl_io_num = TOUCH_SCL_GPIO,
        .sda_pullup_en = GPIO_PULLUP_DISABLE,
        .scl_pullup_en = GPIO_PULLUP_DISABLE,
        .master.clk_speed = TOUCH_I2C_FREQ_HZ,
    };
    if (i2c_param_config(TOUCH_I2C_PORT, &cfg) != ESP_OK) {
        ESP_LOGW(TAG, "touch: i2c_param_config failed");
        return;
    }
    if (i2c_driver_install(TOUCH_I2C_PORT, cfg.mode, 0, 0, 0) != ESP_OK) {
        ESP_LOGW(TAG, "touch: i2c_driver_install failed");
        return;
    }

    vTaskDelay(pdMS_TO_TICKS(50));
    touch_i2c_scan();

    g_touch_addr = 0;
    if (touch_i2c_probe(TOUCH_ADDR1) == ESP_OK) {
        g_touch_addr = TOUCH_ADDR1;
    } else if (touch_i2c_probe(TOUCH_ADDR2) == ESP_OK) {
        g_touch_addr = TOUCH_ADDR2;
    }

    if (g_touch_addr == 0) {
        g_touch_ok = false;
        g_touch_ready = false;
        ESP_LOGW(TAG, "touch: GT911 not found (0x14/0x5D)");
        return;
    }

    uint8_t pid[4] = {0};
    if (touch_i2c_read(0x8140, pid, 4) == ESP_OK) {
        ESP_LOGI(TAG, "touch: GT911 ID=%c%c%c%c addr=0x%02X", pid[0], pid[1], pid[2], pid[3], g_touch_addr);
    } else {
        ESP_LOGW(TAG, "touch: GT911 ID read failed addr=0x%02X", g_touch_addr);
    }

    uint8_t tmp = 0;
    g_touch_ok = (touch_i2c_read(0x814E, &tmp, 1) == ESP_OK);
    g_touch_ready = g_touch_ok;
    ESP_LOGI(TAG, "touch: %s addr=0x%02X", g_touch_ok ? "OK" : "NO", g_touch_addr);
}
static bool touch_read_point(int *x, int *y)
{
    if (!g_touch_ready) return false;
    uint8_t status = 0;
    if (touch_i2c_read(0x814E, &status, 1) != ESP_OK) {
        return false;
    }
    if ((status & 0x80) == 0 || (status & 0x0F) == 0) {
        return false;
    }
    uint8_t buf[8] = {0};
    if (touch_i2c_read(0x8150, buf, sizeof(buf)) != ESP_OK) {
        return false;
    }
    uint8_t clear = 0;
    touch_i2c_write(0x814E, &clear, 1);

    int x1 = (int)(buf[1] << 8 | buf[0]);
    int y1 = (int)(buf[3] << 8 | buf[2]);
    int x2 = (int)(buf[2] << 8 | buf[1]);
    int y2 = (int)(buf[4] << 8 | buf[3]);
    int tx = x1;
    int ty = y1;
    bool x1_ok = (x1 >= 0 && x1 < LCD_H_RES && y1 >= 0 && y1 < LCD_V_RES);
    bool x2_ok = (x2 >= 0 && x2 < LCD_H_RES && y2 >= 0 && y2 < LCD_V_RES);
    if (!x1_ok && x2_ok) { tx = x2; ty = y2; }

    if (TOUCH_SWAP_XY) {
        int t = tx; tx = ty; ty = t;
    }
    if (TOUCH_INVERT_X) tx = LCD_H_RES - 1 - tx;
    if (TOUCH_INVERT_Y) ty = LCD_V_RES - 1 - ty;
    if (tx < 0) tx = 0;
    if (ty < 0) ty = 0;
    if (tx >= LCD_H_RES) tx = LCD_H_RES - 1;
    if (ty >= LCD_V_RES) ty = LCD_V_RES - 1;
    if (x) *x = tx;
    if (y) *y = ty;
    return true;
}
/* ═══════════════════════════════════════════════════════════════════════════
 *  HTTP / Supabase
 * ═══════════════════════════════════════════════════════════════════════════ */
typedef struct {
    char *buf;
    size_t cap;
    size_t len;
    int status;
} http_collect_t;

static esp_err_t http_event_handler(esp_http_client_event_t *evt)
{
    http_collect_t *c = (http_collect_t *)evt->user_data;
    switch (evt->event_id)
    {
    case HTTP_EVENT_ON_CONNECTED:
        ESP_LOGI(TAG, "HTTP event: connected");
        break;
    case HTTP_EVENT_ON_HEADER:
        if (evt->header_key && evt->header_value)
        {
            ESP_LOGI(TAG, "HTTP header: %s: %s", evt->header_key, evt->header_value);
        }
        break;
    case HTTP_EVENT_ON_DATA:
        if (c && evt->data && evt->data_len > 0)
        {
            size_t avail = (c->cap > 0 && c->len < c->cap - 1) ? (c->cap - 1 - c->len) : 0;
            size_t copy = (evt->data_len < (int)avail) ? (size_t)evt->data_len : avail;
            if (copy > 0)
            {
                memcpy(c->buf + c->len, evt->data, copy);
                c->len += copy;
            }
        }
        ESP_LOGI(TAG, "HTTP event: data len=%d", evt->data_len);
        break;
    case HTTP_EVENT_ON_FINISH:
        ESP_LOGI(TAG, "HTTP event: finish");
        break;
    case HTTP_EVENT_DISCONNECTED:
        ESP_LOGI(TAG, "HTTP event: disconnected");
        break;
    default:
        break;
    }
    return ESP_OK;
}

static esp_err_t http_get(const char *url, char *buf, size_t buf_len)
{
    http_collect_t ctx = {
        .buf = buf,
        .cap = buf_len,
        .len = 0,
        .status = 0,
    };

    esp_http_client_config_t cfg = {
        .url               = url,
        .method            = HTTP_METHOD_GET,
        .timeout_ms        = 8000,
        .crt_bundle_attach = esp_crt_bundle_attach,
        .event_handler     = http_event_handler,
        .user_data         = &ctx,
    };
    esp_http_client_handle_t c = esp_http_client_init(&cfg);
    if (!c) return ESP_FAIL;

    ESP_LOGI(TAG, "HTTP GET %s", url);
    esp_http_client_set_header(c, "apikey",        SUPABASE_KEY);
    esp_http_client_set_header(c, "Authorization", "Bearer " SUPABASE_KEY);
    esp_http_client_set_header(c, "Accept",        "application/json");

    esp_err_t err = esp_http_client_perform(c);
    int status = esp_http_client_get_status_code(c);
    ctx.status = status;
    if (err != ESP_OK || status != 200) {
        ESP_LOGW(TAG, "HTTP status=%d err=%s", status, esp_err_to_name(err));
        esp_http_client_cleanup(c);
        return ESP_FAIL;
    }
    if (ctx.len == 0)
    {
        int n = esp_http_client_read_response(c, buf, (int)buf_len - 1);
        ctx.len = (n > 0) ? (size_t)n : 0;
    }
    if (ctx.len > 0 && buf_len > 0)
    {
        buf[ctx.len] = '\0';
    }
    else if (buf_len > 0)
    {
        buf[0] = '\0';
    }
    esp_http_client_cleanup(c);
    if (ctx.len == 0)
    {
        ESP_LOGW(TAG, "HTTP body empty for %s", url);
        return ESP_FAIL;
    }
    ESP_LOGI(TAG, "HTTP body (%u bytes): %.240s", (unsigned)ctx.len, buf);
    return ESP_OK;
}

static bool fetch_field(const char *table, const char *filter,
                        const char *order_col, const char *field,
                        float *out)
{
    char url[512];
    if (filter && filter[0]) {
        snprintf(url, sizeof(url),
                 "%s/rest/v1/%s?sn_number=eq.%s&%s&order=%s.desc&limit=1",
                 SUPABASE_URL, table, SN_NUMBER, filter, order_col);
    } else {
        snprintf(url, sizeof(url),
                 "%s/rest/v1/%s?sn_number=eq.%s&order=%s.desc&limit=1",
                 SUPABASE_URL, table, SN_NUMBER, order_col);
    }

    char *resp = malloc(HTTP_BUF_LEN);
    if (!resp) return false;

    if (http_get(url, resp, HTTP_BUF_LEN) != ESP_OK) { free(resp); return false; }
    ESP_LOGD(TAG, "[%s.%s] %s", table, field, resp);

    cJSON *root = cJSON_Parse(resp);
    free(resp);

    if (!root || !cJSON_IsArray(root) || cJSON_GetArraySize(root) == 0) {
        ESP_LOGW(TAG, "[%s.%s] empty or bad JSON", table, field);
        cJSON_Delete(root);
        return false;
    }
    cJSON *val = cJSON_GetObjectItem(cJSON_GetArrayItem(root, 0), field);
    if (!cJSON_IsNumber(val)) {
        ESP_LOGW(TAG, "[%s.%s] field not a number", table, field);
        cJSON_Delete(root);
        return false;
    }
    *out = (float)val->valuedouble;
    cJSON_Delete(root);
    return true;
}

static bool fetch_all(solar_data_t *d)
{
    bool any = false;
    bool ok;
    ok = fetch_field("BATTERY_INFORMATION", NULL, "created_at", "voltage",  &d->bat_v);
    any |= ok;
    ok = fetch_field("BATTERY_INFORMATION", NULL, "created_at", "current",  &d->bat_i);
    any |= ok;
    ok = fetch_field("BATTERY_INFORMATION", NULL, "created_at", "soc",      &d->bat_soc);
    any |= ok;
    ok = fetch_field("GRID_INFORMATION",    NULL, "created_at", "active_power_pcc_total", &d->grid_pcc_kw);
    any |= ok;
    ok = fetch_field("PV_INFORMATION",      "mppt=eq.1",        "created_at", "power", &d->pv1_kw);
    any |= ok;
    ok = fetch_field("PV_INFORMATION", "mppt=eq.2", "created_at", "power", &d->pv2_kw);
    any |= ok;
    if (any)
    {
        d->fetch_us = esp_timer_get_time();
    }
    return any;
}

static void fetch_task(void *arg)
{
    solar_data_t local = {0};
    while (true) {
        if (wifi_wait_connected(10000)) {
            ESP_LOGI(TAG, "fetch_task: fetching ...");
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(200)) == pdTRUE) {
                local = g_data;
                xSemaphoreGive(g_data_mutex);
            }
            if (fetch_all(&local)) {
                ESP_LOGI(TAG, "fetch OK  SOC=%.0f%%  PV1=%.2fkW  GRID=%.2fkW",
                         local.bat_soc, local.pv1_kw, local.grid_pcc_kw);
                if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(500)) == pdTRUE) {
                    g_data = local;
                    history_push(&g_hist, &local);
                    xSemaphoreGive(g_data_mutex);
                }
                /* Signal main loop: new data available */
                xSemaphoreGive(g_data_ready_sem);
            } else {
                ESP_LOGW(TAG, "fetch_all() partial or failed");
            }
        } else {
            ESP_LOGW(TAG, "fetch_task: WiFi not connected, waiting …");
        }
        vTaskDelay(pdMS_TO_TICKS(FETCH_INTERVAL_MS));
    }
}

static void history_push(history_t *hist, const solar_data_t *d)
{
    if (!hist || !d) return;
    float pv_total = d->pv1_kw + d->pv2_kw;
    float bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    float spotreba = pv_total - bat_kw - d->grid_pcc_kw;

    hist->pv_total[hist->head] = pv_total;
    hist->pv1[hist->head] = d->pv1_kw;
    hist->pv2[hist->head] = d->pv2_kw;
    hist->grid[hist->head] = d->grid_pcc_kw;
    hist->bat_kw[hist->head] = bat_kw;
    hist->spotreba[hist->head] = spotreba;
    hist->soc[hist->head] = d->bat_soc;
    hist->head = (hist->head + 1) % HISTORY_LEN;
    if (hist->count < HISTORY_LEN) hist->count++;
}

static float history_value(const history_t *hist, const float *arr, int i)
{
    if (!hist || !arr || hist->count == 0) return 0.0f;
    int oldest = hist->head - hist->count;
    while (oldest < 0) oldest += HISTORY_LEN;
    int idx = (oldest + i) % HISTORY_LEN;
    return arr[idx];
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  PIXEL PACKING
 * ═══════════════════════════════════════════════════════════════════════════ */
static inline void pack_pixel(uint8_t *dst, uint8_t r, uint8_t g, uint8_t b)
{
#if LCD_BPP == 16
    uint16_t v = ((uint16_t)(r & 0xF8) << 8) |
                 ((uint16_t)(g & 0xFC) << 3) |
                 (uint16_t)(b >> 3);
    dst[0] = v & 0xFF;
    dst[1] = v >> 8;
#else
    dst[0] = r; dst[1] = g; dst[2] = b;
#endif
}

static inline int lcd_bpp(void) { return (LCD_BPP + 7) / 8; }

/* ═══════════════════════════════════════════════════════════════════════════
 *  DRAWING PRIMITIVES
 * ═══════════════════════════════════════════════════════════════════════════ */

/* Fill entire screen with one colour */
static void fill_screen(uint8_t r, uint8_t g, uint8_t b)
{
    const int bpp   = lcd_bpp();
    const int batch = 16;
    size_t sz = (size_t)LCD_H_RES * batch * bpp;
    uint8_t *buf = heap_caps_malloc(sz, MALLOC_CAP_DMA);
    ASSERT_NN(buf);
    for (int i = 0; i < LCD_H_RES * batch; i++) pack_pixel(&buf[i * bpp], r, g, b);

    for (int y = 0; y < LCD_V_RES; y += batch) {
        int ye = y + batch; if (ye > LCD_V_RES) ye = LCD_V_RES;
        CHK(esp_lcd_panel_draw_bitmap(panel, 0, y, LCD_H_RES, ye, buf));
        xSemaphoreTake(vsync_sem, portMAX_DELAY);
    }
    free(buf);
}

/* Draw solid filled rectangle */
static void draw_rect(int x, int y, int w, int h,
                      uint8_t r, uint8_t g, uint8_t b)
{
    if (w <= 0 || h <= 0) return;
    const int bpp   = lcd_bpp();
    const int batch = 8;
    size_t sz = (size_t)w * batch * bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * batch; i++) pack_pixel(&buf[i * bpp], r, g, b);

    int ry = 0;
    while (ry < h) {
        int rows = (ry + batch <= h) ? batch : (h - ry);
        CHK(esp_lcd_panel_draw_bitmap(panel, x, y + ry, x + w, y + ry + rows, buf));
        xSemaphoreTake(vsync_sem, portMAX_DELAY);
        ry += rows;
    }
    free(buf);
}

/* Draw a rounded-corner rectangle frame (no fill, border only) */
static void draw_frame(int x, int y, int w, int h, int thick,
                       uint8_t r, uint8_t g, uint8_t b)
{
    draw_rect(x,         y,          w, thick, r, g, b);   /* top    */
    draw_rect(x,         y + h - thick, w, thick, r, g, b);/* bottom */
    draw_rect(x,         y,          thick, h, r, g, b);   /* left   */
    draw_rect(x + w - thick, y,      thick, h, r, g, b);   /* right  */
}

static void draw_circle_filled(int cx, int cy, int r,
                               uint8_t fr, uint8_t fg, uint8_t fb,
                               uint8_t br, uint8_t bg, uint8_t bb)
{
    if (r <= 0) return;
    int w = r * 2 + 1;
    int h = r * 2 + 1;
    int bpp = lcd_bpp();
    size_t sz = (size_t)w * (size_t)h * (size_t)bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * h; i++) pack_pixel(&buf[i * bpp], br, bg, bb);
    for (int y = -r; y <= r; y++) {
        for (int x = -r; x <= r; x++) {
            if (x * x + y * y <= r * r) {
                int px = x + r;
                int py = y + r;
                size_t idx = ((size_t)py * (size_t)w + (size_t)px) * (size_t)bpp;
                pack_pixel(&buf[idx], fr, fg, fb);
            }
        }
    }
    CHK(esp_lcd_panel_draw_bitmap(panel, cx - r, cy - r, cx + r + 1, cy + r + 1, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
}

static void graph_set_pixel(uint8_t *buf, int w, int h, int x, int y,
                            uint8_t r, uint8_t g, uint8_t b)
{
    if (x < 0 || y < 0 || x >= w || y >= h) return;
    int bpp = lcd_bpp();
    size_t idx = ((size_t)y * (size_t)w + (size_t)x) * (size_t)bpp;
    pack_pixel(&buf[idx], r, g, b);
}

static void graph_draw_line(uint8_t *buf, int w, int h,
                            int x0, int y0, int x1, int y1,
                            uint8_t r, uint8_t g, uint8_t b)
{
    int dx = abs(x1 - x0);
    int sx = x0 < x1 ? 1 : -1;
    int dy = -abs(y1 - y0);
    int sy = y0 < y1 ? 1 : -1;
    int err = dx + dy;
    int x = x0;
    int y = y0;
    while (true) {
        graph_set_pixel(buf, w, h, x, y, r, g, b);
        if (x == x1 && y == y1) break;
        int e2 = 2 * err;
        if (e2 >= dy) { err += dy; x += sx; }
        if (e2 <= dx) { err += dx; y += sy; }
    }
}

static void draw_history_graph(int x, int y, int w, int h, const char *title,
                               const history_t *hist, const float *series,
                               uint8_t lr, uint8_t lg, uint8_t lb)
{
    if (!hist || hist->count < 2 || w <= 4 || h <= 4) return;
    int bpp = lcd_bpp();
    size_t sz = (size_t)w * (size_t)h * (size_t)bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * h; i++) pack_pixel(&buf[i * bpp], C_BG_R, C_BG_G, C_BG_B);
    for (int i = 0; i < w; i++) {
        graph_set_pixel(buf, w, h, i, 0, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, i, h - 1, C_DIV_R, C_DIV_G, C_DIV_B);
    }
    for (int i = 0; i < h; i++) {
        graph_set_pixel(buf, w, h, 0, i, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, w - 1, i, C_DIV_R, C_DIV_G, C_DIV_B);
    }

    float min_v = 1e9f, max_v = -1e9f;
    for (int i = 0; i < hist->count; i++) {
        float v = history_value(hist, series, i);
        if (v < min_v) min_v = v;
        if (v > max_v) max_v = v;
    }
    if (max_v - min_v < 0.01f) { max_v = min_v + 1.0f; }

    int n = hist->count;
    for (int i = 1; i < n; i++) {
        float v0 = history_value(hist, series, i - 1);
        float v1 = history_value(hist, series, i);
        int x0 = (int)((int64_t)(i - 1) * (w - 2) / (n - 1)) + 1;
        int x1 = (int)((int64_t)(i) * (w - 2) / (n - 1)) + 1;
        int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
        int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
        graph_draw_line(buf, w, h, x0, y0, x1, y1, lr, lg, lb);
    }

    CHK(esp_lcd_panel_draw_bitmap(panel, x, y, x + w, y + h, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
    if (title) {
        draw_text(x + 8, y + 6, title, 2, C_MID_R, C_MID_G, C_MID_B, C_BG_R, C_BG_G, C_BG_B);
    }
}

static void draw_history_graph_multi(int x, int y, int w, int h, const char *title,
                                     const history_t *hist,
                                     const float *s1, uint8_t s1r, uint8_t s1g, uint8_t s1b,
                                     const float *s2, uint8_t s2r, uint8_t s2g, uint8_t s2b,
                                     const float *s3, uint8_t s3r, uint8_t s3g, uint8_t s3b)
{
    if (!hist || hist->count < 2 || w <= 4 || h <= 4) return;
    int bpp = lcd_bpp();
    size_t sz = (size_t)w * (size_t)h * (size_t)bpp;
    uint8_t *buf = heap_caps_calloc(1, sz, MALLOC_CAP_DMA);
    if (!buf) return;
    for (int i = 0; i < w * h; i++) pack_pixel(&buf[i * bpp], C_BG_R, C_BG_G, C_BG_B);
    for (int i = 0; i < w; i++) {
        graph_set_pixel(buf, w, h, i, 0, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, i, h - 1, C_DIV_R, C_DIV_G, C_DIV_B);
    }
    for (int i = 0; i < h; i++) {
        graph_set_pixel(buf, w, h, 0, i, C_DIV_R, C_DIV_G, C_DIV_B);
        graph_set_pixel(buf, w, h, w - 1, i, C_DIV_R, C_DIV_G, C_DIV_B);
    }

    float min_v = 1e9f, max_v = -1e9f;
    for (int i = 0; i < hist->count; i++) {
        if (s1) { float v = history_value(hist, s1, i); if (v < min_v) min_v = v; if (v > max_v) max_v = v; }
        if (s2) { float v = history_value(hist, s2, i); if (v < min_v) min_v = v; if (v > max_v) max_v = v; }
        if (s3) { float v = history_value(hist, s3, i); if (v < min_v) min_v = v; if (v > max_v) max_v = v; }
    }
    if (max_v - min_v < 0.01f) { max_v = min_v + 1.0f; }

    int n = hist->count;
    for (int i = 1; i < n; i++) {
        int x0 = (int)((int64_t)(i - 1) * (w - 2) / (n - 1)) + 1;
        int x1 = (int)((int64_t)(i) * (w - 2) / (n - 1)) + 1;
        if (s1) {
            float v0 = history_value(hist, s1, i - 1);
            float v1 = history_value(hist, s1, i);
            int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
            int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
            graph_draw_line(buf, w, h, x0, y0, x1, y1, s1r, s1g, s1b);
        }
        if (s2) {
            float v0 = history_value(hist, s2, i - 1);
            float v1 = history_value(hist, s2, i);
            int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
            int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
            graph_draw_line(buf, w, h, x0, y0, x1, y1, s2r, s2g, s2b);
        }
        if (s3) {
            float v0 = history_value(hist, s3, i - 1);
            float v1 = history_value(hist, s3, i);
            int y0 = h - 2 - (int)(((v0 - min_v) / (max_v - min_v)) * (h - 3));
            int y1 = h - 2 - (int)(((v1 - min_v) / (max_v - min_v)) * (h - 3));
            graph_draw_line(buf, w, h, x0, y0, x1, y1, s3r, s3g, s3b);
        }
    }

    CHK(esp_lcd_panel_draw_bitmap(panel, x, y, x + w, y + h, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
    if (title) {
        draw_text(x + 8, y + 6, title, 2, C_MID_R, C_MID_G, C_MID_B, C_BG_R, C_BG_G, C_BG_B);
    }
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  8×8 BITMAP FONT  (full A-Z, 0-9, punctuation)
 * ═══════════════════════════════════════════════════════════════════════════ */
static const uint8_t *glyph_8x8(char c)
{
    static const uint8_t sp[8]={0};
    static const uint8_t co[8]={0x00,0x18,0x18,0x00,0x18,0x18,0x00,0x00};
    static const uint8_t dt[8]={0x00,0x00,0x00,0x00,0x00,0x18,0x18,0x00};
    static const uint8_t mi[8]={0x00,0x00,0x00,0x7E,0x00,0x00,0x00,0x00};
    static const uint8_t pl[8]={0x00,0x18,0x18,0x7E,0x18,0x18,0x00,0x00};
    static const uint8_t sl[8]={0x02,0x04,0x08,0x10,0x20,0x40,0x00,0x00};
    static const uint8_t pc[8]={0x62,0x64,0x08,0x10,0x26,0x46,0x00,0x00};
    static const uint8_t ex[8]={0x18,0x18,0x18,0x18,0x00,0x18,0x18,0x00};
    static const uint8_t lp[8]={0x0C,0x18,0x30,0x30,0x30,0x18,0x0C,0x00};
    static const uint8_t rp[8]={0x30,0x18,0x0C,0x0C,0x0C,0x18,0x30,0x00};
    static const uint8_t g0[8]={0x3C,0x66,0x6E,0x76,0x66,0x66,0x3C,0x00};
    static const uint8_t g1[8]={0x18,0x38,0x18,0x18,0x18,0x18,0x3C,0x00};
    static const uint8_t g2[8]={0x3C,0x66,0x06,0x0C,0x30,0x60,0x7E,0x00};
    static const uint8_t g3[8]={0x3C,0x66,0x06,0x1C,0x06,0x66,0x3C,0x00};
    static const uint8_t g4[8]={0x0C,0x1C,0x3C,0x6C,0x7E,0x0C,0x0C,0x00};
    static const uint8_t g5[8]={0x7E,0x60,0x7C,0x06,0x06,0x66,0x3C,0x00};
    static const uint8_t g6[8]={0x1C,0x30,0x60,0x7C,0x66,0x66,0x3C,0x00};
    static const uint8_t g7[8]={0x7E,0x66,0x0C,0x18,0x18,0x18,0x18,0x00};
    static const uint8_t g8[8]={0x3C,0x66,0x66,0x3C,0x66,0x66,0x3C,0x00};
    static const uint8_t g9[8]={0x3C,0x66,0x66,0x3E,0x06,0x0C,0x38,0x00};
    static const uint8_t gA[8]={0x18,0x3C,0x66,0x66,0x7E,0x66,0x66,0x00};
    static const uint8_t gB[8]={0x7C,0x66,0x66,0x7C,0x66,0x66,0x7C,0x00};
    static const uint8_t gC[8]={0x3C,0x66,0x60,0x60,0x60,0x66,0x3C,0x00};
    static const uint8_t gD[8]={0x78,0x6C,0x66,0x66,0x66,0x6C,0x78,0x00};
    static const uint8_t gE[8]={0x7E,0x60,0x60,0x7C,0x60,0x60,0x7E,0x00};
    static const uint8_t gF[8]={0x7E,0x60,0x60,0x7C,0x60,0x60,0x60,0x00};
    static const uint8_t gG[8]={0x3C,0x66,0x60,0x6E,0x66,0x66,0x3C,0x00};
    static const uint8_t gH[8]={0x66,0x66,0x66,0x7E,0x66,0x66,0x66,0x00};
    static const uint8_t gI[8]={0x3C,0x18,0x18,0x18,0x18,0x18,0x3C,0x00};
    static const uint8_t gJ[8]={0x1E,0x06,0x06,0x06,0x66,0x66,0x3C,0x00};
    static const uint8_t gK[8]={0x66,0x6C,0x78,0x70,0x78,0x6C,0x66,0x00};
    static const uint8_t gL[8]={0x60,0x60,0x60,0x60,0x60,0x60,0x7E,0x00};
    static const uint8_t gM[8]={0x66,0x7E,0x7E,0x6E,0x66,0x66,0x66,0x00};
    static const uint8_t gN[8]={0x66,0x76,0x7E,0x6E,0x66,0x66,0x66,0x00};
    static const uint8_t gO[8]={0x3C,0x66,0x66,0x66,0x66,0x66,0x3C,0x00};
    static const uint8_t gP[8]={0x7C,0x66,0x66,0x7C,0x60,0x60,0x60,0x00};
    static const uint8_t gQ[8]={0x3C,0x66,0x66,0x66,0x6E,0x3C,0x06,0x00};
    static const uint8_t gR[8]={0x7C,0x66,0x66,0x7C,0x6C,0x66,0x66,0x00};
    static const uint8_t gS[8]={0x3C,0x66,0x60,0x3C,0x06,0x66,0x3C,0x00};
    static const uint8_t gT[8]={0x7E,0x18,0x18,0x18,0x18,0x18,0x18,0x00};
    static const uint8_t gU[8]={0x66,0x66,0x66,0x66,0x66,0x66,0x3C,0x00};
    static const uint8_t gV[8]={0x66,0x66,0x66,0x66,0x66,0x3C,0x18,0x00};
    static const uint8_t gW[8]={0x63,0x63,0x63,0x6B,0x7F,0x77,0x63,0x00};
    static const uint8_t gX[8]={0x66,0x66,0x3C,0x18,0x3C,0x66,0x66,0x00};
    static const uint8_t gY[8]={0x66,0x66,0x66,0x3E,0x06,0x0C,0x38,0x00};
    static const uint8_t gZ[8]={0x7E,0x06,0x0C,0x18,0x30,0x60,0x7E,0x00};

    switch (toupper((unsigned char)c)) {
    case '0': return g0; case '1': return g1; case '2': return g2;
    case '3': return g3; case '4': return g4; case '5': return g5;
    case '6': return g6; case '7': return g7; case '8': return g8;
    case '9': return g9;
    case 'A': return gA; case 'B': return gB; case 'C': return gC;
    case 'D': return gD; case 'E': return gE; case 'F': return gF;
    case 'G': return gG; case 'H': return gH; case 'I': return gI;
    case 'J': return gJ; case 'K': return gK; case 'L': return gL;
    case 'M': return gM; case 'N': return gN; case 'O': return gO;
    case 'P': return gP; case 'Q': return gQ; case 'R': return gR;
    case 'S': return gS; case 'T': return gT; case 'U': return gU;
    case 'V': return gV; case 'W': return gW; case 'X': return gX;
    case 'Y': return gY; case 'Z': return gZ;
    case ':': return co;
    case '.': return dt;
    case '-': return mi;
    case '+': return pl;
    case '/': return sl;
    case '%': return pc;
    case '!': return ex;
    case '(': return lp;
    case ')': return rp;
    default:  return sp;
    }
}

/* ── Scaled text renderer ──────────────────────────────────────────────────── */
static void draw_text(int x, int y, const char *text, uint8_t scale,
                      uint8_t fr, uint8_t fg, uint8_t fb,
                      uint8_t br, uint8_t bg, uint8_t bb)
{
    const size_t len = strlen(text);
    if (!len) return;
    const int W = (int)(len * 8 * scale);
    const int H = 8 * scale;
    const int bpp = lcd_bpp();
    uint8_t *buf = heap_caps_calloc(1, (size_t)W * H * bpp, MALLOC_CAP_DMA);
    ASSERT_NN(buf);

    /* background */
    for (int i = 0; i < W * H; i++) pack_pixel(&buf[i * bpp], br, bg, bb);

    /* glyphs */
    for (size_t ci = 0; ci < len; ci++) {
        const uint8_t *gl = glyph_8x8(text[ci]);
        for (int gy = 0; gy < 8; gy++) {
    uint8_t row = gl[gy];
            for (int gx = 0; gx < 8; gx++) {
                if (!(row & (0x80 >> gx))) continue;
                int px = (int)(ci * 8 * scale + gx * scale);
                int py = gy * scale;
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                        pack_pixel(&buf[((py+sy)*W + (px+sx)) * bpp], fr, fg, fb);
            }
        }
    }

    CHK(esp_lcd_panel_draw_bitmap(panel, x, y, x + W, y + H, buf));
    xSemaphoreTake(vsync_sem, portMAX_DELAY);
    free(buf);
}

/* ── Centre text within a horizontal band ──────────────────────────────────── */
static void draw_text_c(int bx, int bw, int y, const char *text, uint8_t scale,
                        uint8_t fr, uint8_t fg, uint8_t fb,
                        uint8_t br, uint8_t bg, uint8_t bb)
{
    int tw = (int)(strlen(text) * 8 * scale);
    int tx = bx + (bw - tw) / 2;
    if (tx < bx) tx = bx;
    draw_text(tx, y, text, scale, fr, fg, fb, br, bg, bb);
}

/* ── Horizontal progress bar ───────────────────────────────────────────────── */
static void draw_bar(int x, int y, int w, int h,
                     float value, float max_val,
                     uint8_t br, uint8_t bg_c, uint8_t bb)
{
    /* Outer border */
    draw_rect(x, y, w, h, C_DIV_R, C_DIV_G, C_DIV_B);
    /* Dark interior */
    draw_rect(x+2, y+2, w-4, h-4, C_BG_R, C_BG_G, C_BG_B);
    /* Fill */
    if (max_val <= 0) return;
    int fill = (int)((value / max_val) * (float)(w - 6));
    if (fill < 0) fill = 0;
    if (fill > w - 6) fill = w - 6;
    if (fill > 0)
        draw_rect(x+3, y+3, fill, h-6, br, bg_c, bb);
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  SECTION CARD  —  draw a labelled panel with border
 *  Returns the y coordinate of the interior start
 * ═══════════════════════════════════════════════════════════════════════════ */
static void draw_card(int x, int y, int w, int h, const char *title,
                      uint8_t title_r, uint8_t title_g, uint8_t title_b)
{
    /* Card fill */
    draw_rect(x, y, w, h, C_CARD_R, C_CARD_G, C_CARD_B);
    /* 2-px border */
    draw_frame(x, y, w, h, 2, C_DIV_R, C_DIV_G, C_DIV_B);
    /* Top accent stripe */
    draw_rect(x+2, y+2, w-4, 3, title_r, title_g, title_b);
    /* Title text */
    draw_text_c(x, w, y + 10, title, 2,
                title_r, title_g, title_b,
                C_CARD_R, C_CARD_G, C_CARD_B);
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  MAIN UI  —  called on every data refresh
 *
 *  Layout  (720 × 720 px):
 *  ┌──────────────────────────────────────────────────────────────┐
 *  │  HEADER  y:  0 –  76   "SOLAR MONITOR" + subtitle           │
 *  ├───────────────────────────┬──────────────────────────────────┤
 *  │  BATERIA  y: 82 – 420    │  SIET   y:  82 – 260            │
 *  │  LEFT x: 4 – 352         │  RIGHT  x: 368 – 716            │
 *  │                           ├──────────────────────────────────┤
 *  │                           │  SOLAR  y: 264 – 420            │
 *  ├───────────────────────────┴──────────────────────────────────┤
 *  │  ENERGY FLOW bar   y: 424 – 480                             │
 *  ├──────────────────────────────────────────────────────────────┤
 *  │  STATUS BAR        y: 484 – 720  (WiFi, SN, spotreba, age) │
 *  └──────────────────────────────────────────────────────────────┘
 * ═══════════════════════════════════════════════════════════════════════════ */
static void draw_header(bool wifi_ok)
{
    draw_rect(0, 0, 720, 44, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_rect(0, 0, 720, 2, C_AMB_R, C_AMB_G, C_AMB_B);
    draw_text(12, 12, "neststatsV2", 2, C_BRT_R, C_BRT_G, C_BRT_B, C_HDR_R, C_HDR_G, C_HDR_B);

    uint8_t lr = wifi_ok ? 0x00 : 0xFF;
    uint8_t lg = wifi_ok ? 0xFF : 0x33;
    uint8_t lb = wifi_ok ? 0x66 : 0x33;
    draw_text(628, 10, "LIVE", 2, C_MID_R, C_MID_G, C_MID_B, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_circle_filled(705, 16, 4, lr, lg, lb, C_HDR_R, C_HDR_G, C_HDR_B);

    const int bx = 500, by = 8, bw = 110, bh = 26;
    draw_rect(bx, by, bw, bh, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_frame(bx, by, bw, bh, 2, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text_c(bx, bw, by + 5, g_auto_rotate ? "PAUSE" : "PLAY", 2,
                C_AMB_R, C_AMB_G, C_AMB_B, C_CARD_R, C_CARD_G, C_CARD_B);
}
static void draw_tab_bar(page_t page)
{
    const int y = 44;
    const int h = 28;
    const int margin = 6;
    const int gap = 6;
    int total = 720 - margin * 2 - gap * (PAGE_COUNT - 1);
    int tab_w = total / PAGE_COUNT;
    int x = margin;
    draw_rect(0, y, 720, h, C_CARD_R, C_CARD_G, C_CARD_B);
    for (int i = 0; i < PAGE_COUNT; i++) {
        if (i == page) {
            draw_rect(x, y + 2, tab_w, h - 4, C_AMB_R, C_AMB_G, C_AMB_B);
        }
        draw_text_c(x, tab_w, y + 6, PAGE_NAMES[i], 2,
                    C_BRT_R, C_BRT_G, C_BRT_B, C_CARD_R, C_CARD_G, C_CARD_B);
        x += tab_w + gap;
    }
}

static bool handle_touch(page_t *page)
{
    int tx = 0, ty = 0;
    static int64_t last_touch_us = 0;
    if (!touch_read_point(&tx, &ty)) return false;
    int64_t now = esp_timer_get_time();
    if (now - last_touch_us < 250000) return false;
    last_touch_us = now;

    /* PLAY / PAUSE button */
    if (tx >= 500 && tx <= 500 + 110 && ty >= 8 && ty <= 8 + 26) {
        g_auto_rotate = !g_auto_rotate;
        return true;
    }

    /* Tabs */
    if (ty >= 44 && ty <= 44 + 28) {
        const int margin = 6;
        const int gap = 6;
        int total = 720 - margin * 2 - gap * (PAGE_COUNT - 1);
        int tab_w = total / PAGE_COUNT;
        int x = margin;
        for (int i = 0; i < PAGE_COUNT; i++) {
            if (tx >= x && tx <= x + tab_w) {
                if (page) *page = (page_t)i;
                return true;
            }
            x += tab_w + gap;
        }
    }
    return false;
}

static void draw_battery_card(const solar_data_t *d, int x, int y)
{
    const int W = 348;
    const int H = 414;
    char s[64];
    draw_card(x, y, W, H, "BATERIA", C_GRN_R, C_GRN_G, C_GRN_B);

    snprintf(s, sizeof(s), "%.0f%%", d->bat_soc);
    {
    uint8_t sr, sg, sb;
        if      (d->bat_soc >= 65) { sr=0x00; sg=0xFF; sb=0x55; }
        else if (d->bat_soc >= 30) { sr=0xFF; sg=0xAA; sb=0x00; }
        else                       { sr=0xFF; sg=0x22; sb=0x22; }
        draw_text_c(x, W, y + 36, s, 9, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
        draw_text_c(x, W, y + 111, "STAV NABITIA", 2, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
    }

    {
    uint8_t br2, bg2, bb2;
        if      (d->bat_soc >= 65) { br2=0x00; bg2=0xBB; bb2=0x44; }
        else if (d->bat_soc >= 30) { br2=0xFF; bg2=0x88; bb2=0x00; }
        else                       { br2=0xCC; bg2=0x11; bb2=0x11; }
        draw_bar(x+10, y+133, W-20, 22, d->bat_soc, 100.0f, br2, bg2, bb2);
    }

    draw_rect(x+10, y+163, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text_c(x, W, y+172, "NAPATIE  [V]", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.2f", d->bat_v);
    draw_text_c(x, W, y+191, s, 6, C_BRT_R, C_BRT_G, C_BRT_B, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(x+10, y+245, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text_c(x, W, y+254, "PRUD  [A]", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%+.2f", d->bat_i);
    draw_text_c(x, W, y+273, s, 6, C_BRT_R, C_BRT_G, C_BRT_B, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(x+10, y+327, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    float bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    {
        const char *state;
        uint8_t sr, sg, sb;
        if      (bat_kw >  0.05f) { state="NABIJANIE"; sr=0x00; sg=0xFF; sb=0x66; }
        else if (bat_kw < -0.05f) { state="VYBIJANIE"; sr=0x00; sg=0xCC; sb=0xFF; }
        else                      { state="STANDBY";   sr=0x88; sg=0x88; sb=0x88; }
        draw_text_c(x, W, y+337, "VYKON  [kW]", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
        snprintf(s, sizeof(s), "%+.3f", bat_kw);
        draw_text_c(x, W, y+356, s, 5, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
        draw_text_c(x, W, y+393, state, 2, sr, sg, sb, C_CARD_R, C_CARD_G, C_CARD_B);
    }
}

static void draw_grid_card(const solar_data_t *d, int x, int y)
{
    const int W = 348;
    const int H = 176;
    char s[64];
    draw_card(x, y, W, H, "GRID", C_CYN_R, C_CYN_G, C_CYN_B);
    {
        const char *dir;
        uint8_t dr, dg, db;
        if      (d->grid_pcc_kw >  0.05f) { dir="ODBER ZO SIETE"; dr=0xFF; dg=0x77; db=0x00; }
        else if (d->grid_pcc_kw < -0.05f) { dir="DODAVKA DO SIETE"; dr=0x00; dg=0xFF; db=0x66; }
        else                               { dir="VYVAZENE"; dr=0x88; dg=0x88; db=0x88; }
        draw_text_c(x, W, y + 34, dir, 2, dr, dg, db, C_CARD_R, C_CARD_G, C_CARD_B);
    }
    snprintf(s, sizeof(s), "%+.3f", d->grid_pcc_kw);
    draw_text_c(x, W, y + 54, s, 6, C_CYN_R, C_CYN_G, C_CYN_B, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_text_c(x, W, y + 120, "kilowatt", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    {
        float ag = d->grid_pcc_kw < 0 ? -d->grid_pcc_kw : d->grid_pcc_kw;
        uint8_t br2 = (d->grid_pcc_kw >= 0) ? 0xFF : 0x00;
        uint8_t bg2 = (d->grid_pcc_kw >= 0) ? 0x77 : 0xFF;
        uint8_t bb2 = 0x22;
        draw_bar(x+10, y + 142, W-20, 22, ag, 12.0f, br2, bg2, bb2);
    }
}

static void draw_solar_card(const solar_data_t *d, int x, int y)
{
    const int W = 348;
    const int H = 232;
    char s[64];
    draw_card(x, y, W, H, "SOLAR  FV", C_YLW_R, C_YLW_G, C_YLW_B);
    draw_text(x+14, y + 32, "STRING 1:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.3f kW", d->pv1_kw);
    draw_text_c(x, W, y + 52, s, 4, C_YLW_R, C_YLW_G, C_YLW_B, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_bar(x+10, y + 87, W-20, 16, d->pv1_kw, 8.0f, C_YLW_R, C_YLW_G, 0x00);

    draw_rect(x+10, y + 110, W-20, 1, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text(x+14, y + 118, "STRING 2:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.3f kW", d->pv2_kw);
    draw_text_c(x, W, y + 138, s, 4, 0xFF, 0xCC, 0x44, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_bar(x+10, y + 174, W-20, 16, d->pv2_kw, 8.0f, 0xFF, 0xCC, 0x00);
}

static void __attribute__((unused)) draw_energy_flow_bar(const solar_data_t *d, int y)
{
    char s[64];
    draw_rect(0, y, 720, 58, C_CARD_R, C_CARD_G, C_CARD_B);
    draw_rect(0, y, 720, 2,  C_AMB_R,  C_AMB_G,  C_AMB_B);
    draw_rect(0, y + 56, 720, 2,  C_AMB_R,  C_AMB_G,  C_AMB_B);

    float pv_total  = d->pv1_kw + d->pv2_kw;
    float bat_kw = (d->bat_v * d->bat_i) / 1000.0f;
    float spotreba  = pv_total - bat_kw - d->grid_pcc_kw;

    draw_text(16, y + 8, "SPOLU FV:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.2f kW", pv_total);
    draw_text(16, y + 26, s, 3, C_YLW_R, C_YLW_G, C_YLW_B, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(238, y + 6, 2, 44, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text(254, y + 8, "SPOTREBA:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%.2f kW", spotreba);
    draw_text(254, y + 26, s, 3, 0xFF, 0xAA, 0xFF, C_CARD_R, C_CARD_G, C_CARD_B);

    draw_rect(478, y + 6, 2, 44, C_DIV_R, C_DIV_G, C_DIV_B);
    draw_text(494, y + 8, "BAT VYKON:", 2, C_MID_R, C_MID_G, C_MID_B, C_CARD_R, C_CARD_G, C_CARD_B);
    snprintf(s, sizeof(s), "%+.2f kW", bat_kw);
    {
    uint8_t vr = (bat_kw >= 0) ? 0x00 : 0x00;
        uint8_t vg = (bat_kw >= 0) ? 0xFF : 0xCC;
        uint8_t vb = (bat_kw >= 0) ? 0x66 : 0xFF;
        draw_text(494, y + 26, s, 3, vr, vg, vb, C_CARD_R, C_CARD_G, C_CARD_B);
    }
}

static void draw_dashboard_page(const solar_data_t *d)
{
    draw_battery_card(d, 4, 82);
    draw_grid_card(d, 368, 82);
    draw_solar_card(d, 368, 264);
    draw_history_graph_multi(8, 506, 704, 200, "PV / GRID / SPOTREBA (kW)",
                             &g_hist,
                             g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B,
                             g_hist.grid, C_CYN_R, C_CYN_G, C_CYN_B,
                             g_hist.spotreba, 0xFF, 0xAA, 0xFF);
}

static void draw_battery_page(const solar_data_t *d)
{
    draw_battery_card(d, 186, 82);
    draw_history_graph(8, 506, 704, 100, "SOC (%)",
                       &g_hist, g_hist.soc, C_GRN_R, C_GRN_G, C_GRN_B);
    draw_history_graph(8, 612, 704, 100, "BAT VYKON (kW)",
                       &g_hist, g_hist.bat_kw, C_CYN_R, C_CYN_G, C_CYN_B);
}

static void draw_grid_page(const solar_data_t *d)
{
    draw_grid_card(d, 186, 82);
    draw_history_graph(8, 260, 704, 200, "GRID (kW)",
                       &g_hist, g_hist.grid, C_CYN_R, C_CYN_G, C_CYN_B);
    draw_history_graph(8, 480, 704, 200, "SPOTREBA (kW)",
                       &g_hist, g_hist.spotreba, 0xFF, 0xAA, 0xFF);
}

static void draw_pv_page(const solar_data_t *d)
{
    draw_solar_card(d, 186, 82);
    draw_history_graph_multi(8, 330, 704, 190, "PV TOTAL/STRINGS (kW)",
                             &g_hist,
                             g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B,
                             g_hist.pv1, 0xFF, 0xCC, 0x44,
                             g_hist.pv2, 0xFF, 0x99, 0x11);
    draw_history_graph(8, 540, 704, 170, "PV TOTAL (kW)",
                       &g_hist, g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B);
}

static void draw_stats_page(const solar_data_t *d)
{
    (void)d;
    draw_history_graph_multi(8, 110, 704, 190, "PV / GRID / SPOTREBA (kW)",
                             &g_hist,
                             g_hist.pv_total, C_YLW_R, C_YLW_G, C_YLW_B,
                             g_hist.grid, C_CYN_R, C_CYN_G, C_CYN_B,
                             g_hist.spotreba, 0xFF, 0xAA, 0xFF);
    draw_history_graph(8, 320, 704, 160, "SOC (%)",
                       &g_hist, g_hist.soc, C_GRN_R, C_GRN_G, C_GRN_B);
    draw_history_graph(8, 500, 704, 180, "BAT VYKON (kW)",
                       &g_hist, g_hist.bat_kw, C_GRN_R, C_GRN_G, C_GRN_B);
}
static void draw_ui(const solar_data_t *d, bool wifi_ok)
{
    fill_screen(C_BG_R, C_BG_G, C_BG_B);
    draw_header(wifi_ok);
    draw_tab_bar((page_t)g_page_index);

    switch ((page_t)g_page_index) {
    case PAGE_BAT:
        draw_battery_page(d);
        break;
    case PAGE_GRID:
        draw_grid_page(d);
        break;
    case PAGE_PV:
        draw_pv_page(d);
        break;
    case PAGE_STATS:
        draw_stats_page(d);
        break;
    case PAGE_DASH:
    default:
        draw_dashboard_page(d);
        break;
    }
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  BOOT SPLASH
 * ═══════════════════════════════════════════════════════════════════════════ */
static void draw_boot_splash(const char *status_msg)
{
    fill_screen(C_BG_R, C_BG_G, C_BG_B);

    /* Header */
    draw_rect(0, 0, 720, 80, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_rect(0, 0, 720,  4, C_AMB_R, C_AMB_G, C_AMB_B);
    draw_rect(0, 76, 720, 4, C_AMB_R, C_AMB_G, C_AMB_B);
    draw_text_c(0, 720, 15, "SOLAR MONITOR", 4, 0xFF, 0xD7, 0x00, C_HDR_R, C_HDR_G, C_HDR_B);
    draw_text_c(0, 720, 55, "STARTOVANIE SYSTEMU", 2, C_MID_R, C_MID_G, C_MID_B, C_HDR_R, C_HDR_G, C_HDR_B);

    /* Big icon area */
    draw_text_c(0, 720, 260, "FV  BATERIA  GRID", 3, C_AMB_R, C_AMB_G, C_AMB_B, C_BG_R, C_BG_G, C_BG_B);
    draw_text_c(0, 720, 310, status_msg, 3, C_BRT_R, C_BRT_G, C_BRT_B, C_BG_R, C_BG_G, C_BG_B);
    draw_text_c(0, 720, 360, "PROSIM CAKAJTE", 2, C_DIM_R, C_DIM_G, C_DIM_B, C_BG_R, C_BG_G, C_BG_B);

    /* SN bottom */
    draw_rect(0, 690, 720, 30, C_CARD_R, C_CARD_G, C_CARD_B);
    char sn[64];
    snprintf(sn, sizeof(sn), "SN: %s", SN_NUMBER);
    draw_text_c(0, 720, 697, sn, 2, C_DIM_R, C_DIM_G, C_DIM_B, C_CARD_R, C_CARD_G, C_CARD_B);
}

/* ═══════════════════════════════════════════════════════════════════════════
 *  app_main
 * ═══════════════════════════════════════════════════════════════════════════ */
void app_main(void)
{
    printf("\r\n");
    printf("  +------------------------------------------+\r\n");
    printf("  |   SOLAR MONITOR  v3.0  (bug-fixed)       |\r\n");
    printf("  |   ESP32-P4  |  ST7703  |  720x720 DSI    |\r\n");
    printf("  +------------------------------------------+\r\n\r\n");

    /* �� LCD ��������������������������������������������������������������� */
    ESP_LOGI(TAG, "Initialising LCD �");
    lcd_init();
    CHK(esp_lcd_dpi_panel_set_pattern(panel, MIPI_DSI_PATTERN_NONE));
    touch_init();

    /* �� Boot splash ����������������������������������������������������� */
    draw_boot_splash("PRIPAJANIE WIFI");

    /* �� WiFi ������������������������������������������������������������� */
    wifi_init_sta();
    bool wifi_ok = wifi_wait_connected(20000);
    if (wifi_ok)
        ESP_LOGI(TAG, "WiFi connected");
    else
        ESP_LOGW(TAG, "WiFi connect timeout � reason %u", wifi_fail_reason);

    draw_boot_splash(wifi_ok ? "NACITAVAM DATA" : "WIFI ZLYHALO");

    /* �� Shared state ����������������������������������������������������� */
    g_data_mutex   = xSemaphoreCreateMutex();
    g_data_ready_sem = xSemaphoreCreateCounting(8, 0);   /* counting semaphore */
    ASSERT_NN(g_data_mutex);
    ASSERT_NN(g_data_ready_sem);

    /* �� BUG FIX: Synchronous initial fetch BEFORE starting background task.
     *            This guarantees the first draw always has real data.       */
    if (wifi_ok) {
        ESP_LOGI(TAG, "Initial synchronous fetch �");
        solar_data_t init = {0};
        if (fetch_all(&init)) {
            ESP_LOGI(TAG, "Initial fetch OK � SOC=%.0f%% PV1=%.2fkW PV2=%.2fkW GRID=%.2fkW",
                     init.bat_soc, init.pv1_kw, init.pv2_kw, init.grid_pcc_kw);
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(500)) == pdTRUE) {
                g_data = init;
                history_push(&g_hist, &init);
                xSemaphoreGive(g_data_mutex);
            }
        } else {
            ESP_LOGW(TAG, "Initial fetch partial � some values may be 0");
        }
    }

    /* �� Background refresh task ����������������������������������������� */
    xTaskCreate(fetch_task, "fetch_task", 10240, NULL, 5, NULL);

    /* �� First UI render ������������������������������������������������� */
    solar_data_t snap = {0};
    if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(300)) == pdTRUE) {
        snap = g_data;
        xSemaphoreGive(g_data_mutex);
    }
    draw_ui(&snap, wifi_ok);

    g_last_page_us = esp_timer_get_time();

    /* �� Main loop: data + touch + auto-rotate ��������������������������� */
    while (true) {
        bool dirty = false;
        if (xSemaphoreTake(g_data_ready_sem, pdMS_TO_TICKS(200))) {
            dirty = true;
        }

        page_t next = (page_t)g_page_index;
        if (handle_touch(&next)) {
            if ((int)next != g_page_index) {
                g_page_index = (int)next;
            }
            g_last_page_us = esp_timer_get_time();
            dirty = true;
        }

        if (g_auto_rotate) {
            int64_t now = esp_timer_get_time();
            if ((now - g_last_page_us) > (int64_t)PAGE_SWITCH_MS * 1000) {
                g_page_index = (g_page_index + 1) % PAGE_COUNT;
                g_last_page_us = now;
                dirty = true;
            }
        }

        if (dirty) {
            wifi_ok = wifi_is_connected();
            if (xSemaphoreTake(g_data_mutex, pdMS_TO_TICKS(300)) == pdTRUE) {
                snap = g_data;
                xSemaphoreGive(g_data_mutex);
            }
            draw_ui(&snap, wifi_ok);
        }
    }
}

#endif /* SOC_MIPI_DSI_SUPPORTED */




















