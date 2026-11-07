# Sinophone

[English](README.en.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.md)

一個 [Dalamud](https://github.com/goatcorp/Dalamud) 插件，為 **Final Fantasy XIV** 顯示遊戲文字的官方簡體（CN）或繁體（TC）中文翻譯。涵蓋：

- 對話視窗上方的對話翻譯，或直接寫入遊戲本身的對話框。
- 選擇對話框，例如是／否、選項清單與圖示選項。
- NPC 與物件上方的姓名板。
- 道具提示中的道具名稱。

覆蓋視窗由插件自行繪製。原生模式會把中文寫入遊戲本身的視窗，兩者都需要字型模組（見[字型設定](#字型設定)）。

## 遊戲語言支援

目前只支援 **英文** 遊戲客戶端：插件以遊戲顯示的英文文字比對，再對應到中文。

其他三種遊戲語言——**日文、德文、法文**——已在規劃中。

## 相容性

Sinophone 只改變畫面上顯示的內容，不會修改遊戲資料，也不會修改其他插件讀取到的文字，因此其他插件看到的仍是英文。可以和其他插件一起使用。

## 需求

- Dalamud API 15（`Dalamud.NET.SDK/15.0.0`）
- .NET 10（`net10.0-windows7.0`）

## 安裝

Sinophone 不在 Dalamud 官方插件清單中。請把它加入自訂插件來源：

1. 開啟 Dalamud 設定（`/xlsettings`），前往 **Experimental** → **Custom Plugin Repositories**。
2. 加入 `https://raw.githubusercontent.com/raydenhui/Sinophone/main/pluginmaster.json` 並儲存。
3. 開啟插件安裝程式，搜尋 **Sinophone** 並安裝。

首次使用時，執行 `/sinophone` 並點一下 **Download / update data**。翻譯資料另行下載，因此不必更新插件也能取得新的翻譯資料。

## 字型設定

覆蓋視窗不需要這一步：它會從 Windows 載入中文字型（或使用插件設定中的字型路徑）。以下設定只給原生模式使用：原生模式會把翻譯寫入遊戲本身的視窗，需要遊戲自己畫出中文。

遊戲的字型檔沒有中文字元，因此要用含中文字型的字型檔取代。從 [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts) 挑一個：該倉庫有多種字型，每個資料夾內含一個 `.ttmp2` 模組包。接著用下列任一工具安裝。

### TexTools

1. 安裝 [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI/releases)。
2. 匯入 `.ttmp2`，讓它修改遊戲檔案。

### Penumbra

1. 安裝 [Penumbra](https://github.com/xivdev/Penumbra) 並啟用。
2. 匯入同一個 `.ttmp2` 並啟用。若字型沒有改變，開啟 Penumbra 的 **Settings** 分頁，前往 **Advanced**，點一下 **Reload Fonts**，不需重啟遊戲。

### 裝好之後

原生模式預設為關閉。在 `/sinophone` 中開啟你要用的原生模式。若沒有安裝字型模組，原生模式的每一行都會顯示缺字符號，而不是中文。

## 狀態

Sinophone 現在已可正常使用。偶爾還是會遇到錯誤與缺漏：CN 與 TC 的資料匯出進度落後於線上遊戲，因此新增或改寫的台詞可能沒有翻譯，部分介面也仍是英文。設定與翻譯資料格式在版本之間仍可能變動。

若某一行沒有翻譯，或發現顯示異常，請開啟 [issue](https://github.com/raydenhui/Sinophone/issues)。也歡迎在同一個地方提出功能需求。

## 致謝

Sinophone 建立在他人的成果之上。感謝：

- [Dalamud](https://github.com/goatcorp/Dalamud)：插件框架、addon 生命週期事件與 ImGui 封裝。
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)：原生模式讀寫的遊戲結構。
- [ImGui](https://github.com/ocornut/imgui)：覆蓋視窗。
- [ffxiv-datamining-cn](https://github.com/thewakingsands/ffxiv-datamining-cn) 與
  [ffxiv-datamining-tc](https://github.com/thewakingsands/ffxiv-datamining-tc)：中文遊戲資料。
- [xivapi/ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining)：英文 CSV 與腳本匯出。
- [OpenCC](https://github.com/BYVoid/OpenCC)：簡繁轉換表。見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
- [AtmoOmen/FFXIVFonts](https://github.com/AtmoOmen/FFXIVFonts)：讓遊戲能畫出中文的字型模組包。
- [TexTools](https://github.com/TexTools/FFXIV_TexTools_UI) 與
  [Penumbra](https://github.com/xivdev/Penumbra)：安裝該字型模組包的兩種方式。
- [FFXIVChnTextPatch-GP](https://github.com/GpointChen/FFXIVChnTextPatch-GP)：工作表清單，以及哪些遊戲文字值得翻譯的參考。
- [Echoglossian](https://github.com/lokinmodar/Echoglossian)：這個插件的想法來源。
- [fftrans](https://github.com/raydocs/fftrans)：另一個獨立的 Talk 處理器，確認了相同的重新寫入方式。

中文文字本身來自遊戲官方的簡體與繁體在地化內容。

## 授權

MIT — 見 [LICENSE](LICENSE)。
