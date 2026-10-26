# 電子書閱讀器

Windows 桌面電子書閱讀器，以 Electron 打造，排版引擎使用 [foliate-js](https://github.com/johnfactotum/foliate-js)。

## 支援格式

| 格式 | 說明 |
| --- | --- |
| EPUB | EPUB 2 / 3，含固定版面（漫畫、繪本） |
| PDF | 透過 pdf.js 繪製，可選取文字 |
| AZW3 | Kindle KF8 |
| AZW / MOBI / PRC | Mobipocket（舊版 Kindle） |
| FB2 / FBZ / CBZ | 附帶支援 |

受 DRM 保護的檔案（從 Kindle 商店或 Adobe DRM 購買的書）無法開啟，程式會偵測並提示。KFX 格式不支援。

## 功能

- **書架**：封面、作者、閱讀進度；可搜尋、排序；右鍵可在檔案總管中顯示或從書架移除
- **開啟方式**：按鈕、拖曳檔案到視窗、雙擊已關聯的檔案；一次拖入多本會全部加入書架
- **自動記住閱讀位置**，下次開啟從上次的地方繼續
- **目錄、書籤、全文搜尋**（PDF 也能搜尋）
- **閱讀設定**：5 種主題（白／米黃／護眼綠／深色／純黑）、字體大小、行距、字型（明體／黑體／楷體）、分頁或捲動、單欄或雙欄、版心寬度、頁邊距
- **PDF**：符合頁面／符合寬度／自訂縮放、可選雙頁並排，深色主題會自動反轉頁面顏色
- **全螢幕沉浸模式**：工具列自動隱藏，滑鼠移到上下邊緣時顯示

## 快捷鍵

| 按鍵 | 功能 |
| --- | --- |
| ← → / PageUp PageDown / 空白鍵 | 翻頁 |
| 滑鼠滾輪、點擊頁面左右兩側 | 翻頁 |
| Ctrl + 滾輪、Ctrl + = / − / 0 | 放大／縮小字體（PDF 為縮放） |
| Ctrl + F | 全文搜尋 |
| Ctrl + D | 加入／移除書籤 |
| T | 開關目錄 |
| F11 | 全螢幕 |
| Esc | 關閉面板 → 離開全螢幕 → 回到書架 |
| Ctrl + O | 開啟檔案 |

## 開發

```bash
npm install
npm start          # 開發模式執行（F12 開啟 DevTools）
npm run dist       # 產生 dist/EbookReader-Setup-x.y.z.exe 與 EbookReader-Portable-x.y.z.exe
```

> 在 VS Code 的整合終端機中若無法啟動，請先清除 `ELECTRON_RUN_AS_NODE` 環境變數。

設定與書架資料存放在 `%APPDATA%\電子書閱讀器\library.json`。可用環境變數 `EBOOK_READER_USER_DATA` 指定其他位置。

## 程式結構

```
main.js          主程序：視窗、app:// 協定、檔案讀取、單一執行個體、檔案關聯
preload.cjs      提供給畫面的 window.api
src/index.html   介面
src/app.js       書架、閱讀器、目錄、書籤、搜尋、設定
src/style.css    樣式與主題
src/foliate/     foliate-js（MIT），本地修改記錄於 src/foliate/VERSION
build/           安裝程式圖示
```
