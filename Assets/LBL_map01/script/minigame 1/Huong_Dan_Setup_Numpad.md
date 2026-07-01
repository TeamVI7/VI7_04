# Hướng dẫn setup Canvas Numpad (bấm chuột, không cần bàn phím)

Mục tiêu: dựng lại Canvas trong scene `Table > Canvas` thành một bàn phím số giống ảnh mẫu (ENTER PASSCODE + numpad 0-9 + DEL/OK + panel KEYPAD LOCKED), toàn bộ thao tác bằng click chuột.

---

## 0. Chuẩn bị

- Mở scene, tìm object `Table > Canvas` (Canvas World Space đang có sẵn `UI Input Blocker`, `World Space UI Setup`, `Morse Minigame Manager`).
- **Không xóa Canvas cũ** — chỉ xóa/thay các con bên trong nó (InputField, Enter Button cũ...). Giữ nguyên component `Canvas`, `Canvas Scaler`, `UI Input Blocker`, `World Space UI Setup`, `Morse Minigame Manager` trên chính GameObject Canvas.
- Đã thay file `MorseMinigameManager.cs` bằng bản mới (script đã gửi ở trên).

---

## 1. Tạo Panel_Screen (nền màn hình)

1. Chuột phải vào `Canvas` → UI → **Panel** → đổi tên `Panel_Screen`.
2. Xóa component `Image` mặc định màu trắng nếu có, thêm `Image` màu nền tối (vd R20 G20 B25, Alpha 255) — hoặc dùng sprite/gradient nếu có sẵn asset.
3. RectTransform: Anchor = stretch full, hoặc set kích thước cố định theo màn hình computer (vd 400x300), căn giữa Canvas.

Tất cả các phần tử bên dưới đều là **con của `Panel_Screen`**.

---

## 2. Tạo Text tiêu đề

1. Chuột phải `Panel_Screen` → UI → **Text - TextMeshPro** → đổi tên `Text_Title`.
   - Text: `ENTER PASSCODE`
   - Font size lớn, màu trắng/xám nhạt, neo góc trên-trái.
2. Tạo thêm 1 TMP Text nữa → `Text_SubTitle`.
   - Text: `ACCESS PROTECTED`
   - Font size nhỏ hơn, màu xám mờ, nằm dưới Title.

---

## 3. Tạo màn hình hiển thị số đã nhập (Display)

1. Chuột phải `Panel_Screen` → UI → **Panel** → đổi tên `Panel_Display`.
   - Đây chính là ô hiển thị số (vd "045" trong ảnh mẫu).
   - Thêm `Image` làm nền — **đây là object sẽ gắn vào field `Display Background`**.
2. Chuột phải `Panel_Display` → UI → **Text - TextMeshPro** → đổi tên `Text_Display`.
   - Để trống text lúc đầu (script sẽ tự điền).
   - Font size to, canh giữa hoặc canh phải, đây là **`Display Text`**.
3. (Tuỳ chọn) Thêm `Text_Feedback` (TMP Text) nhỏ đặt cạnh display để hiện "✓ ĐÚNG" / "✗ SAI" — đây là **`Feedback Text`**.

---

## 4. Tạo khu vực Numpad (12 nút: 1-9, 0, DEL, OK)

### 4.1 Tạo container với Grid Layout

1. Chuột phải `Panel_Screen` → Create Empty (hoặc UI → Panel không cần Image) → đổi tên `Panel_Numpad`.
2. Add Component → **Grid Layout Group**.
   - Cell Size: vd `80 x 60` (tùy kích thước Canvas của bạn).
   - Spacing: vd `8 x 8`.
   - Constraint: `Fixed Column Count` = `3`.
3. (Tuỳ chọn) Add Component **Content Size Fitter** nếu muốn tự co giãn theo nội dung.

### 4.2 Tạo 1 nút mẫu rồi nhân bản

1. Chuột phải `Panel_Numpad` → UI → **Button - TextMeshPro** → đổi tên `Btn_1`.
2. Sửa text con bên trong thành `1`, chỉnh font/màu cho giống ảnh (số to, nền tối, khi hover sáng lên).
3. Chọn `Btn_1` → Ctrl+D (Duplicate) → tạo tiếp `Btn_2` ... `Btn_9`, `Btn_0`, sửa lại text từng nút.
   - Vì có `Grid Layout Group` trên `Panel_Numpad`, các nút sẽ **tự xếp thành lưới 3 cột** theo đúng thứ tự trong Hierarchy — kéo thả sắp xếp lại thứ tự nếu muốn giống bàn phím điện thoại (1 2 3 / 4 5 6 / 7 8 9 / DEL 0 OK).
4. Duplicate thêm 2 nút cuối:
   - `Btn_Del` → text `DEL`
   - `Btn_OK` → text `OK`

> Mẹo: có thể biến `Btn_1` thành **Prefab** trước khi nhân bản để sau này sửa style 1 chỗ áp dụng cho tất cả.

---

## 5. Tạo Panel_Locked ("KEYPAD LOCKED")

1. Chuột phải `Panel_Screen` → UI → **Panel** → đổi tên `Panel_Locked`.
   - Đặt đè lên toàn bộ hoặc một phần màn hình (giống dải hồng "KEYPAD LOCKED" trong ảnh).
   - Image nền màu đỏ/hồng nhạt.
2. Thêm con TMP Text bên trong: text `KEYPAD LOCKED`.
3. **Tắt Active** object này (checkbox ở góc trên Inspector) — script sẽ tự `SetActive(true)` khi nhập sai, `SetActive(false)` khi reset.

---

## 6. Gắn reference vào script `Morse Minigame Manager`

Chọn lại object `Canvas` (nơi có component `Morse Minigame Manager`), kéo các object vừa tạo vào đúng field:

| Field trong Inspector | Kéo object nào vào |
|---|---|
| `Password` | giữ nguyên, vd `0451` |
| `Door` | object cửa (đã có sẵn) |
| `Display Text` | `Text_Display` |
| `Display Background` | `Panel_Display` (chính object có Image) |
| `Feedback Text` | `Text_Feedback` (nếu có) |
| `Digit Buttons` (size = 10) | kéo `Btn_0` → `Btn_9` vào từng ô |
| `Del Button` | `Btn_Del` |
| `Ok Button` | `Btn_OK` |
| `Locked Panel` | `Panel_Locked` |

---

## 7. Gắn OnClick cho từng nút số (0-9)

**Chỉ áp dụng cho `Btn_0` → `Btn_9`** (DEL và OK không cần làm bước này vì script tự add listener bằng code).

Với mỗi nút số, ví dụ `Btn_7`:

1. Chọn `Btn_7` trong Hierarchy.
2. Ở Inspector, component `Button` → mục `On Click ()` → bấm nút `+`.
3. Kéo object `Canvas` (chứa `Morse Minigame Manager`) vào ô Object.
4. Ở dropdown chọn hàm: `MorseMinigameManager` → **Dynamic string** → `OnDigitPressed`.
5. Ô text bên cạnh gõ đúng số của nút: `7`.

Lặp lại cho tất cả 10 nút, **nhớ gõ đúng số tương ứng** (nút `Btn_0` → gõ `0`, `Btn_5` → gõ `5`, v.v.). Đây là bước dễ sai nhất, nên kiểm tra kỹ.

---

## 8. Kiểm tra các script liên quan (không cần sửa, chỉ cần đảm bảo đã gắn đúng)

- `ComputerInteraction`: field `Game Manager` vẫn trỏ vào `Canvas` (nơi có `Morse Minigame Manager`) — giữ nguyên.
- `UI Input Blocker`: giữ nguyên, không liên quan tới việc đổi InputField → Numpad.
- `World Space UI Setup` / `MinigameCameraRaycaster`: giữ nguyên, chúng lo việc raycast từ `minigame camera` vào Canvas — Button vẫn nhận click bình thường như InputField trước đây.
- `ForceRaycastTarget` (nếu có gắn vào Canvas): vẫn hữu ích, tự bật `Raycast Target = true` cho tất cả Image/Text con — giúp các nút Btn_0-9 chắc chắn nhận được click.

---

## 9. Test

1. Play scene → bấm `E` nhìn vào computer để mở UI (vẫn theo cơ chế cũ, chỉ có phần nhập password là đổi).
2. Bấm số bằng chuột → `Text_Display` phải cập nhật realtime (vd "0", "04", "045"...).
3. Nhập đủ 4 số (bằng `password.Length`) → tự động check, không cần bấm OK.
   - Đúng → nền display xanh, chữ "✓ ĐÚNG", cửa mở sau `doorOpenDelay`.
   - Sai → `Panel_Locked` hiện lên, nền display nhấp nháy đỏ, các nút tạm khóa (`interactable = false`) trong `lockedDuration` giây, sau đó tự reset về rỗng.
4. Nút `DEL` xóa từng ký tự cuối; nút `OK` xác nhận thủ công (dùng khi muốn bấm sớm hơn khi chưa đủ số, hoặc double-check).

---

## 10. Lỗi thường gặp

| Hiện tượng | Nguyên nhân thường gặp |
|---|---|
| Bấm nút không có phản ứng gì | Chưa gắn `OnDigitPressed` + số đúng ở bước 7, hoặc collider 3D (monitor) chưa bị `ComputerInteraction` tắt khi vào UI |
| `Text_Display` không hiện số | Chưa kéo đúng object vào field `Display Text` |
| `Panel_Locked` không tắt lại sau khi sai | Đảm bảo `Panel_Locked` để **Active = false** ban đầu trong Scene (không phải disable component, mà là uncheck object) |
| Nút không sáng hover / không bấm được | Kiểm tra `ForceRaycastTarget` đã gắn vào Canvas hoặc tự set `Raycast Target = true` cho Image của từng Button |
| Cả 12 nút dính lại thành 1 cụm | Kiểm tra `Grid Layout Group` trên `Panel_Numpad`: Cell Size/Spacing phải > 0, RectTransform của `Panel_Numpad` phải đủ rộng |
