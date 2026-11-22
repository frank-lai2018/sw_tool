# MD Reader

適用 Windows 10 / 11（64 位元）的 Markdown 桌面閱讀器。繁體中文介面，文件在本機處理，不需要帳號或網路。

新電腦準備、完整操作方式與常見問題，請參閱 [使用說明](使用說明.md)。發送免安裝版時，可一併提供 `dist/使用說明.md`。

## 使用

雙擊 `dist/MD-Reader-1.3.1-Windows.exe` 即可啟動，不需安裝。首次啟動需要一點時間解壓縮。

- 點「開啟文件」、按 Ctrl+O，或拖曳 `.md` 檔案到視窗。
- 按 Alt 顯示選單，選「Windows 整合」→「加入檔案右鍵選單」，即可對 Markdown 檔按右鍵「使用 MD Reader 開啟」，並從「開啟方式」選擇閱讀器。Windows 11 可能需先點「顯示更多選項」。搬移 EXE 或更新版本後請重新加入，以更新路徑；同一選單也可移除整合。
- 可多開：重複執行 EXE，或點左側「新視窗」／按 Ctrl+N，讓不同視窗閱讀不同文件。關閉其中一個視窗不會關閉其他視窗。
- 自動產生章節目錄，支援 GFM 表格、待辦清單、程式碼高亮及複製。
- 點右上角月亮切換主題，A− / A＋調整字體。
- 左側目錄與閱讀區之間的垂直分隔線可左右拖曳，調整目錄寬度並自動保存；雙擊恢復預設。也支援方向鍵、Shift＋方向鍵及 Home／End。
- 閱讀區上方的「頁寬」滑桿可在 600–1600 px 之間調整頁面最大寬度；點「滿版」填滿閱讀區，再點一次回到滑桿的設定。寬度設定會自動保存，視窗較窄時頁面會自動縮小。
- Ctrl+F 搜尋；Enter 下一個，Shift+Enter 上一個，Esc 關閉。
- Ctrl+R 重新載入已在外部編輯的檔案。
- Ctrl+P 列印；在 Windows 列印視窗選擇 Microsoft Print to PDF 可另存 PDF。
- 原始碼按鈕可切換 Markdown 純文字檢視。
- 開啟文件後按「編輯」修改 Markdown 原始碼，按「預覽」查看排版，按「儲存」或 Ctrl+S 寫回原檔。切換文件、重新載入或關閉視窗前，會確認是否捨棄未儲存的變更。
- 大型文件自動分頁，只建立目前頁面的內容，支援跨頁目錄及全文搜尋；原始碼也採分頁且按需顯示。大型文件的列印僅列印目前頁，執行前會提示範圍。
- 最近開啟、字體、頁寬、目錄寬度、主題會保存，啟動時會嘗試開啟上次文件。
- 命令列：`"MD-Reader-1.3.1-Windows.exe" "C:\筆記\readme.md"`。

支援 `.md`、`.markdown`、`.mdown`、`.mkd`、`.txt`。自動辨識 UTF-8、含 BOM 的 UTF-16；非 UTF-8 檔案嘗試以 Big5 開啟。單份文件上限 100 MiB。

1.3.1 將檔案上限由 20 MiB 提高至 100 MiB，大型文件採分頁閱讀。開檔失敗時會持續顯示檔案路徑與原因；啟動時開檔失敗也不會繼續顯示歡迎範例。

## 圖片與連結

本機圖片須使用相對路徑，位於文件所在資料夾或其子資料夾，支援 PNG、JPEG、GIF、WebP、AVIF、BMP，單張上限 15 MB。網路圖片不會自動連線下載；SVG 圖片、數學公式與 Mermaid 圖表目前不支援。網頁連結使用預設瀏覽器，相對 Markdown 連結在閱讀器內開啟。

儲存時保留 UTF-8 或 UTF-16 編碼，Big5 文件會先確認是否轉為 UTF-8。若磁碟上的檔案已有變更，程式會保留草稿並提示。最近開啟紀錄與偏好儲存在 Electron 的使用者資料目錄（一般為 `%APPDATA%\md-reader\settings.json`）。執行檔未進行數位簽章，Windows 可能顯示來源提示。

## 開發與打包

需要 Node.js 22.12+，建議在 Windows 64 位元執行。

```powershell
npm install
npm test
npm start
npm run build
```

`npm run build` 產生單一可攜式 Windows EXE，包含執行環境，使用者不需安裝 Node.js。

打包後可執行 `npm run test:desktop` 驗證桌面功能，或執行 `node test/portable.cjs` 驗證單一 EXE 的解壓縮、中文路徑開檔與正常關閉。測試使用獨立的暫存設定目錄，畫面截圖放在 `test-output/`。

## 技術

Electron、Marked、DOMPurify、Highlight.js。啟用隔離環境、停用 renderer Node.js；Markdown HTML 經過清理，禁止內嵌腳本、任意導航及遠端資源。使用套件的授權詳見套件內的 LICENSE。
