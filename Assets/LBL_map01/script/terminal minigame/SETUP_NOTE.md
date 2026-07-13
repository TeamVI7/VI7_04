# Setup chi tiết – Minigame Fallout Terminal (Unity)

Ghi chú này hướng dẫn từng bước dựng minigame `FalloutTerminalManager.cs` +
`TerminalWordSlot.cs` trong Unity, và nối nó với `ServerMinigameManager.cs`
đã có sẵn.

---

## 0. Chuẩn bị

- Unity đã cài **TextMeshPro** (Window → TextMeshPro → Import TMP Essential
  Resources, nếu chưa có).
- Đã có sẵn 2 file script: `FalloutTerminalManager.cs`, `TerminalWordSlot.cs`
  và file `ServerMinigameManager.cs` bạn đã upload — bỏ cả 3 vào thư mục
  `Assets/Scripts/`.

---

## 1. Tạo World Space Canvas làm "màn hình" terminal

1. Trong Hierarchy: **UI → Canvas**. Đặt tên `TerminalCanvas`.
2. Chọn `TerminalCanvas` → Inspector → component **Canvas**:
   - `Render Mode` → **World Space**.
3. Đặt `Rect Transform` của Canvas (VD `Width 800, Height 600`), rồi kéo
   Canvas này ra đặt ngay trước mặt cái máy server/terminal trong scene,
   chỉnh `Scale` nhỏ lại (VD `0.002, 0.002, 0.002`) cho vừa kích thước thật.
4. Thêm component **Canvas Scaler** (nếu chưa có) → để mặc định là được.
5. Đảm bảo Canvas có component **Graphic Raycaster** (tự thêm sẵn khi tạo
   Canvas) — cần cái này để bấm được Button.

> Lưu ý: Vì là World Space nên player cần dùng chuột thật (Cursor không bị
> khoá) hoặc bạn cần bắn tia (Physics Raycaster + EventSystem) nếu chơi ở
> góc nhìn FPS. Nếu game của bạn là FPS, nhớ mở khoá con trỏ chuột
> (`Cursor.lockState = CursorLockMode.None; Cursor.visible = true;`) ngay
> khi mở terminal, và khoá lại khi đóng terminal.

---

## 2. Dựng layout bên trong Canvas

Bên trong `TerminalCanvas`, tạo các phần tử sau (tất cả là con của Canvas):

| Tên GameObject       | Loại UI            | Vai trò                                   |
|-----------------------|---------------------|--------------------------------------------|
| `Background`          | Image               | Nền đen/xanh cho giống terminal            |
| `TitleText`            | TextMeshPro - Text  | Dòng tiêu đề trên cùng (đổi chữ khi thắng) |
| `AttemptsText`         | TextMeshPro - Text  | "LƯỢT THỬ CÒN LẠI: 4"                      |
| `HistoryText`          | TextMeshPro - Text  | Log các lần đoán (nên đặt cuộn được)       |
| `WordGrid`             | Empty + Layout Group| Chứa các Button từ mật khẩu                |

### 2.1. Tạo `WordGrid`
1. Tạo Empty GameObject tên `WordGrid`, con của Canvas.
2. Thêm component **Grid Layout Group** (hoặc 2 `Vertical Layout Group`
   riêng cho 2 cột nếu muốn giống layout 2 cột thật của Fallout).
3. Chỉnh `Cell Size`, `Spacing` sao cho vừa với số lượng Button bạn định
   dùng (khuyên dùng 10–16 Button để giống bản gốc).

### 2.2. Tạo Button mẫu (Word Slot)
1. Trong `WordGrid`: **UI → Button - TextMeshPro**. Đặt tên `WordSlot`.
2. Xoá `Image` mặc định hoặc đổi màu nền thành trong suốt/đen để chữ nổi
   trên nền terminal.
3. Chỉnh `TMP_Text` con: font monospace (kiểu chữ máy tính), màu xanh lá
   (`#33FF4C` hoặc tương tự), cỡ chữ vừa đủ đọc.
4. Gắn script **`TerminalWordSlot.cs`** vào chính GameObject `WordSlot`
   (nó sẽ tự lấy component `Button` vì có `[RequireComponent(typeof(Button))]`).
   Kéo `TMP_Text` con vào ô **Label** trong Inspector (nếu Awake tự tìm
   không đúng cái bạn muốn).
5. Kéo `WordSlot` ra thành **Prefab** (kéo thả vào thư mục `Assets/Prefabs`).
6. **Nhân bản (Ctrl+D)** prefab này ra 10–16 bản, đặt tên `WordSlot (1)`,
   `WordSlot (2)`... xếp vào trong `WordGrid` (Grid Layout Group sẽ tự sắp
   xếp vị trí).

> Vì `FalloutTerminalManager` KHÔNG tự sinh Button lúc runtime — bạn phải tạo
> sẵn số lượng Button đủ dùng ngay trong Editor, đúng như ghi trong comment
> đầu file script.

---

## 3. Gắn `FalloutTerminalManager`

1. Tạo Empty GameObject, đặt tên `FalloutTerminal`.
2. Gắn script `FalloutTerminalManager.cs` vào.
3. Trong Inspector, kéo/điền các ô sau:

### a. Ngân hàng từ mật khẩu
- `Word Pool`: nhập danh sách từ (khuyên nên có nhiều từ **cùng độ dài**,
  VD toàn bộ 6 ký tự: `SERVER, ROUTER, MEMORY, BUFFER, KERNEL...`).

### b. Cấu hình ván chơi
- `Max Attempts`: số lượt thử (mặc định 4, bản gốc trên itch.io dùng **5**
  — bạn có thể đổi thành 5 nếu muốn giống hệt).
- `Enable Dud Removal Brackets`: bật nếu muốn có tính năng bấm cặp ngoặc
  `[ ] ( ) < > { }` để loại 1 từ sai, không tốn lượt.
- `Max Dud Brackets`: số cặp ngoặc tối đa xuất hiện mỗi ván.
- `Junk Characters`: các ký tự rác dùng để lấp đầy dòng chữ.

### c. Slots trên Canvas
- `Word Slots`: kéo **TẤT CẢ** GameObject `WordSlot (0..n)` bạn vừa tạo ở
  bước 2.2 vào mảng này.

### d. UI phụ
- `Terminal Message Text` ← kéo `TitleText`.
- `Attempts Text` ← kéo `AttemptsText`.
- `History Text` ← kéo `HistoryText`.
- `Terminal Panel` ← kéo chính `TerminalCanvas` (hoặc 1 GameObject cha bọc
  toàn bộ UI) — dùng để ẩn/hiện khi mở/đóng terminal.

### e. Nội dung hiển thị
- `Intro Message`: chữ hiển thị lúc bắt đầu.
- `Solved Message`: dòng NGẮN hiển thị ngay trên terminal nhỏ khi vừa thắng
  (VD `"TRUY CẬP ĐƯỢC CẤP"`) — hiển thị trong lúc `Close Delay After Solve`
  giây trước khi terminal tự đóng lại.
- `Locked Message`: chữ hiển thị khi thua/hết lượt.

### f. Màn hình cảnh báo lớn (bật riêng khi giải xong)

Đây là phần MỚI thay cho việc chỉ đổi chữ trong terminal nhỏ — giờ khi
giải xong, một **màn hình to riêng** sẽ bật lên đè lên toàn bộ game để
hiển thị cảnh báo, giống kiểu "system override" trong phim.

1. Tạo 1 Canvas **RIÊNG**, khác với `TerminalCanvas`:
   - `UI → Canvas`, đặt tên `BigScreenCanvas`.
   - `Render Mode` → **Screen Space - Overlay** (để nó phủ toàn màn hình,
     không bị giới hạn trong không gian 3D như terminal nhỏ).
   - Đặt `Sort Order` cao hơn Canvas khác (VD `10`) để luôn hiện trên cùng.
2. Bên trong, tạo:
   - `Background`: 1 `Image` full màn hình, màu đen/đỏ mờ (Alpha ~200) để
     tạo cảm giác cảnh báo khẩn cấp.
   - `WarningText`: 1 `TextMeshPro - Text`, cỡ chữ LỚN, màu đỏ hoặc xanh
     nhấp nháy, canh giữa màn hình.
   - (Tuỳ chọn) thêm hiệu ứng nhấp nháy bằng 1 script `Animator` hoặc
     `CanvasGroup` fade cho kịch tính hơn.
3. **Tắt `BigScreenCanvas` (SetActive(false))** trong Editor — nó sẽ được
   script tự bật lên khi cần, không nên để hiện sẵn lúc bắt đầu game.
4. Quay lại `FalloutTerminalManager` trong Inspector, ở mục
   "Màn hình cảnh báo lớn":
   - `Big Screen Panel` ← kéo `BigScreenCanvas` vào.
   - `Big Screen Text` ← kéo `WarningText` vào.
   - `Big Screen Message`: nội dung hiển thị, mặc định đã để sẵn
     `"CẢNH BÁO / BẠN PHẢI NGẮT KẾT NỐI MÁY CHỦ ĐỂ TẮT ĐƯỢC AI!"` — sửa
     lại tuỳ ý.
   - `Auto Hide Big Screen`: bật nếu muốn màn hình tự tắt sau vài giây;
     tắt nếu muốn player phải tự bấm nút đóng (xem bước 5).
   - `Big Screen Auto Hide Delay`: số giây hiển thị trước khi tự ẩn (chỉ
     dùng khi `Auto Hide Big Screen` = true).
   - `Big Screen Sound`: âm thanh báo động phát lúc màn hình bật lên.

5. (Tuỳ chọn) Thêm nút "Đóng" nếu bạn tắt `Auto Hide Big Screen`:
   - Tạo 1 Button trong `BigScreenCanvas`.
   - Trong `OnClick()` của Button, kéo GameObject `FalloutTerminal` vào,
     chọn hàm `FalloutTerminalManager.HideBigScreen()`.

### Thứ tự diễn ra khi player thắng (đã cập nhật):
1. Terminal nhỏ hiện dòng `Solved Message` (VD "TRUY CẬP ĐƯỢC CẤP").
2. Sau `Close Delay After Solve` giây → terminal nhỏ tự đóng lại.
3. `BigScreenCanvas` bật lên full màn hình, hiện `Big Screen Message`
   ("BẠN PHẢI NGẮT KẾT NỐI MÁY CHỦ ĐỂ TẮT AI!") + phát `Big Screen Sound`.
4. Sau `Delay Before Server Rise` giây → server bắt đầu trồi lên
   (`ServerMinigameManager.OnPlayerEnterTrigger()`).
5. Nếu `Auto Hide Big Screen` = true → sau `Big Screen Auto Hide Delay`
   giây, màn hình cảnh báo tự tắt. Nếu false → player phải tự bấm nút đóng.

### g. Fix lỗi UI / Raycast (dùng lại 3 script có sẵn)

Nếu bạn từng gặp lỗi Canvas World Space không bấm được (bị HUD khác che,
bị Collider 3D chắn raycast, hoặc Graphic quên bật Raycast Target) — dùng
lại 3 script bạn đã có, gắn 1 lần rồi kéo vào `FalloutTerminalManager`:

1. **`UIInputBlocker.cs`** — gắn vào 1 GameObject bất kỳ trong scene (VD
   `FalloutTerminal` luôn cũng được). Điền `Objects To Disable` (súng,
   script điều khiển player...) nếu muốn tắt khi mở terminal.
   → Kéo object này vào ô **`Ui Input Blocker`** trong `FalloutTerminalManager`.
2. **`ForceRaycastTarget.cs`** — gắn thẳng vào `TerminalCanvas` (Canvas
   World Space chứa các WordSlot).
   → Kéo `TerminalCanvas` vào ô **`Force Raycast Target`**.
3. **`AutoDisableBlockingColliders.cs`** — gắn vào camera dùng để raycast
   vào minigame (camera có `PhysicsRaycaster`). Điền `Ui Canvas` = chính
   `TerminalCanvas`.
   → Kéo GameObject camera đó vào ô **`Auto Disable Blocking Colliders`**.

Cả 3 ô này đều **tuỳ chọn** — để trống nếu không dùng, `FalloutTerminalManager`
sẽ tự bỏ qua (dùng toán tử `?.` nên không lỗi). Khi đã gán, script tự gọi:
- `StartTerminal()` → `BlockInput()` → `DisableBlockers()` → `FixAll()`
  (mở cursor/tắt HUD → tắt collider chắn raycast → đảm bảo Button bấm được).
- `CloseTerminal()` → `UnblockInput()` → `RestoreBlockers()`
  (khôi phục HUD/collider về như cũ).

Nhờ vậy bạn không cần tự gọi tay 3 script này ở đâu khác nữa — mọi thứ
diễn ra tự động mỗi lần mở/đóng terminal.

### h. Liên kết Minigame Server
- `Server Minigame Manager`: kéo GameObject đang gắn
  `ServerMinigameManager.cs` (cái quản lý 6 khối server) vào đây.
- `Delay Before Server Rise`: số giây chờ sau khi thắng rồi mới cho server
  trồi lên (mặc định 1.5s).
- `Close Terminal After Solve` + `Close Delay After Solve`: có tự đóng màn
  hình terminal sau khi thắng không, và chờ bao lâu.

### i. Âm thanh (tuỳ chọn)
- Kéo `AudioSource` (hoặc để trống, script tự thêm) và các `AudioClip`
  vào: `Click Sound`, `Correct Sound`, `Wrong Sound`, `Dud Removed Sound`,
  `Locked Sound`.

---

## 4. Kích hoạt terminal khi player tương tác

`FalloutTerminalManager` không tự mở màn hình — bạn cần 1 script Interact
(ví dụ player lại gần bấm phím `E`) gọi:

```csharp
falloutTerminalManager.StartTerminal();
```

Ví dụ script Interact tối giản, gắn vào chính cái máy terminal (có Collider
dạng Trigger):

```csharp
using UnityEngine;

public class TerminalInteract : MonoBehaviour
{
    public FalloutTerminalManager terminal;
    public KeyCode interactKey = KeyCode.E;

    private bool _playerInRange = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) _playerInRange = false;
    }

    private void Update()
    {
        if (_playerInRange && Input.GetKeyDown(interactKey))
            terminal.StartTerminal();
    }
}
```

---

## 5. Kiểm tra (checklist trước khi test)

- [ ] Canvas ở chế độ **World Space**, có **Graphic Raycaster**.
- [ ] Scene có **EventSystem** (Unity tự tạo khi bạn thêm Canvas đầu tiên —
      kiểm tra Hierarchy có GameObject `EventSystem` chưa).
- [ ] Tất cả `WordSlot` đã kéo vào mảng `Word Slots` của
      `FalloutTerminalManager`.
- [ ] `Word Pool` có **ít nhất 2 từ cùng độ dài** — nếu không script sẽ báo
      lỗi `LogError` và không chạy được ván chơi.
- [ ] `Server Minigame Manager` đã được gán, để lúc thắng server trồi lên
      đúng như mong muốn.
- [ ] `Terminal Panel` đã gán đúng object cần ẩn/hiện.
- [ ] `Big Screen Panel` (Canvas Screen Space - Overlay) đã tạo, để mặc
      định **tắt** trong scene, và đã gán vào `Big Screen Panel` +
      `Big Screen Text` trong Inspector.
- [ ] Player có thể bấm chuột vào Button trên Canvas (test bằng cách mở
      Cursor nếu là game FPS/khoá chuột).

---

## 6. Các ý mở rộng (tuỳ chọn, có thể làm sau)

- **Nhấn Escape để restart, tính là thua** (giống bản gốc trên itch.io):
  thêm đoạn code trong `Update()` của `FalloutTerminalManager` kiểm tra
  `Input.GetKeyDown(KeyCode.Escape)` → gọi `Lock()` rồi `SetupNewGame()`.
- **Đổi `Max Attempts` thành 5** để giống hệt bản gốc.
- **Thêm nhiều từ vào `Word Pool`** theo từng nhóm độ dài khác nhau để ván
  chơi đa dạng hơn.
- **VFX/âm thanh khi mở terminal**: gọi thêm hiệu ứng trong `StartTerminal()`.

Nếu bạn muốn mình code sẵn 2 ý đầu (Escape-restart + 5 lượt) thì báo mình,
mình sẽ cập nhật thẳng vào `FalloutTerminalManager.cs`.
