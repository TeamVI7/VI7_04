# Setup Minigame Nối Dây (Wire Puzzle) — kiểu Among Us

## Tổng quan luồng hoạt động

```
Player lại gần hộp điện + bấm E
        │
        ▼
WireBoxInteraction.EnterWireBox()
   - Mở UI Screen Space (wirePuzzleUIRoot)
   - inputBlocker.BlockInput() (mở cursor, tắt HUD/gun...)
   - puzzleManager.ResetPuzzle()
        │
        ▼
Player kéo chuột nối từng dây (trái → phải, đúng màu)
        │
        ▼  (khi đủ 4/4 dây đúng)
WirePuzzleManager bắn event OnPuzzleCompleted
        │
        ▼
WireBoxInteraction.HandleSolved()
   - Mở khoá ComputerInteraction (enabled = true)
   - Bật đèn Morse / Sequencer (BeginSequence())
   - Tự đóng UI sau 1 giây
        │
        ▼
Player giờ bấm E ở Computer được → giải morse → SlidingDoorController mở cửa
```

Trước khi nối dây xong: **ComputerInteraction bị `enabled = false`** → người chơi
không bấm E mở computer được, và đèn Morse ở trạng thái tắt/idle (đỏ tối).

---

## Các file liên quan

| File | Vai trò |
|---|---|
| `WireConnectionPoint.cs` | Gắn vào **mỗi điểm tròn** nối dây (trái + phải) |
| `WirePuzzleManager.cs` | Gắn vào **Panel chứa puzzle**, xử lý kéo-thả, vẽ dây, kiểm tra đúng/sai |
| `WireBoxInteraction.cs` | Gắn vào **GameObject hộp điện** (3D, có Collider), xử lý E để mở UI |
| `MorseLightSequencer.cs` | **Bản đã sửa** — thêm `activateOnStart` để không tự chạy lúc Start |

> ⚠️ File `MorseLightSequencer.cs` mới này **thay thế** file cũ bạn đang có (đã thêm
> `activateOnStart`, `BeginSequence()`, `StopSequence()`). Toàn bộ API cũ vẫn giữ nguyên,
> không ảnh hưởng nếu bạn không dùng Sequencer.

---

## BƯỚC 1 — Tạo Canvas UI nối dây (Screen Space)

1. Hierarchy → UI → **Canvas** → đặt tên `WirePuzzleCanvas`
   - Render Mode: **Screen Space - Overlay** (đơn giản nhất, không cần camera)
2. Trong `WirePuzzleCanvas`, tạo 1 **Panel** con tên `WirePanel`
   - Đây là cái bạn sẽ gắn `WirePuzzleManager` vào (xem Bước 3)
   - Set màu nền tối/mờ để giống "bảng điện"
3. `WirePuzzleCanvas` để **active = true** trong scene, nhưng `WirePanel`
   sẽ bị `WireBoxInteraction` set `SetActive(false)` lúc Start — không cần tự tắt tay.

---

## BƯỚC 2 — Tạo các điểm nối dây

Mỗi dây cần **2 điểm**: 1 bên trái (nguồn), 1 bên phải (đích).

Với 4 dây (đỏ, vàng, xanh dương, xanh lá) → cần **8 điểm tròn**.

### Cách tạo 1 điểm:
1. Trong `WirePanel`, UI → **Image** → đặt tên ví dụ `Source_Red`
2. Set hình tròn (sprite tròn hoặc dùng Image mặc định, chỉnh kích thước ~40x40)
3. Tick **Raycast Target** = true (mặc định đã true)
4. Add Component → `WireConnectionPoint`
   - `wireId` = `red`
   - `isSourceSide` = ✅ true (vì đây là bên trái)
5. Đặt vị trí bên trái panel (cố định, không random theo yêu cầu của bạn)

### Lặp lại cho bên phải (đích):
- Đặt tên `Target_Red`
- `wireId` = `red` (PHẢI khớp với bên trái để được tính là 1 cặp đúng)
- `isSourceSide` = ❌ false
- Đặt vị trí bên phải panel

### Lặp lại tương tự cho 3 màu còn lại:
- `Source_Yellow` / `Target_Yellow` → wireId = `yellow`
- `Source_Blue` / `Target_Blue` → wireId = `blue`
- `Source_Green` / `Target_Green` → wireId = `green`

> 💡 Gợi ý layout: xếp 4 điểm Source theo cột dọc bên trái, 4 điểm Target
> theo cột dọc bên phải, nhưng **đảo thứ tự màu khác với bên trái** để puzzle
> có độ khó (ví dụ trái: đỏ-vàng-xanh dương-xanh lá, phải: xanh lá-đỏ-xanh dương-vàng).
> Vì bạn chọn "không random", thứ tự này sẽ cố định mãi — chỉnh 1 lần trong Editor là xong.

---

## BƯỚC 3 — Tạo line container + prefab dây

### 3a. Line container (nơi chứa các dây được vẽ ra)
1. Trong `WirePanel`, tạo 1 **Empty RectTransform** tên `LineContainer`
   - UI → Rectangle hoặc tạo Empty GameObject rồi Add Component `RectTransform`
   - Set Anchor = stretch full (để chứa toạ độ đúng theo panel)
   - **Quan trọng**: kéo `LineContainer` này lên **TRÊN CÙNG thứ tự sibling**
     nhưng **DƯỚI** các điểm nối trong Hierarchy nếu muốn dây vẽ đè lên điểm,
     hoặc **TRÊN** các điểm nếu muốn điểm đè lên dây (thường để dây ở dưới,
     điểm tròn ở trên để dễ click). → Đặt `LineContainer` là **object đầu tiên**
     trong `WirePanel`, các điểm nối tạo sau (nằm trên trong thứ tự vẽ).

### 3b. Prefab 1 đoạn dây
1. Trong `LineContainer`, UI → **Image** → đặt tên `WireLineSegment`
   - Pivot của RectTransform: set **(0, 0.5)** (mép trái-giữa) — quan trọng để
     code stretch đúng hướng
   - Anchor: (0, 0.5) cả 2
   - Width/Height tuỳ ý (code sẽ override `sizeDelta` lúc runtime)
2. Kéo `WireLineSegment` ra ngoài Hierarchy thành **Prefab** (kéo vào folder Assets)
3. Sau khi tạo prefab xong, **xoá `WireLineSegment` khỏi scene** (không cần để lại,
   chỉ cần asset prefab) — vì `WirePuzzleManager` sẽ `Instantiate` nó lúc runtime.
4. Set prefab này **inactive** (uncheck active) để không hiện lúc chưa dùng tới —
   code sẽ tự `SetActive(true)` khi instantiate.

---

## BƯỚC 4 — Gắn `WirePuzzleManager`

1. Chọn `WirePanel` → Add Component → `WirePuzzleManager`
2. Trong Inspector:
   - `Connection Points`: để **trống** (code tự tìm qua `GetComponentsInChildren`),
     hoặc kéo tay 8 điểm vào nếu muốn kiểm soát thứ tự
   - `Line Container`: kéo `LineContainer` vào
   - `Wire Line Prefab`: kéo prefab `WireLineSegment` vào
   - `Wire Thickness`: 8 (chỉnh theo ý thích)
   - `Wire Colors`: mặc định có sẵn 4 màu `red/yellow/blue/green` — chỉnh lại
     nếu bạn dùng tên `wireId` khác hoặc muốn đổi màu RGB cụ thể
   - `Complete Panel`: (tuỳ chọn) kéo 1 GameObject "✓ Hoàn thành!" nếu muốn
     hiện chữ báo thành công trước khi UI tự đóng
   - `Parent Canvas`: để trống, code tự tìm Canvas cha (`WirePuzzleCanvas`)

> Component này tự gọi `ApplyColors()` lúc Awake → màu của từng điểm tròn
> (Image color) sẽ tự đổi theo `wireId` tương ứng trong bảng `Wire Colors`,
> bạn không cần tự tô màu tay cho từng điểm trong Editor.

---

## BƯỚC 5 — Gắn `WireBoxInteraction` vào hộp điện

1. Chọn GameObject 3D "hộp điện" (model trong scene, phải có **Collider**)
2. Add Component → `WireBoxInteraction`
3. Inspector:
   - `Player Camera Transform`: kéo Main Camera của player (hoặc để trống,
     code tự lấy `Camera.main`)
   - `Interaction Distance`: 2.5 (chỉnh theo ý)
   - `Interactable Layer`: chọn layer chứa hộp điện (giống cách bạn set
     cho `ComputerInteraction`)
   - `Wire Puzzle UI Root`: kéo `WirePanel` (hoặc `WirePuzzleCanvas` nếu
     bạn muốn tắt cả canvas luôn — khuyến nghị kéo `WirePanel` vì Canvas
     để active liên tục cho nhẹ)
   - `Puzzle Manager`: kéo `WirePanel` (object có `WirePuzzleManager`) vào
   - `Input Blocker`: kéo `UIInputBlocker` đang dùng cho computer (dùng
     **chung 1 cái** với computer minigame, không cần tạo cái mới)
   - `Computer Interaction To Lock`: kéo GameObject có script `ComputerInteraction`
     vào (field này nhận `MonoBehaviour` nên kéo đúng component `ComputerInteraction`)
   - `Morse Lights To Activate`: kéo tất cả `MorseLightController` trong scene vào
     **HOẶC**
   - `Morse Sequencer To Activate`: kéo `MorseLightSequencer` vào — **chỉ chọn 1
     trong 2 cách này** tuỳ bạn đang dùng đèn đơn hay sequencer nhiều đèn

### ⚠️ Nếu dùng `MorseLightSequencer`:
Vào GameObject có `MorseLightSequencer` → trong Inspector, **tick OFF**
`Activate On Start`. Đây là field MỚI vừa thêm — nếu không tắt, đèn Morse
sẽ tự chạy ngay từ đầu game, không chờ nối dây xong.

### Nếu dùng từng `MorseLightController` riêng (không qua Sequencer):
Không cần chỉnh gì thêm — `WireBoxInteraction.Start()` sẽ tự gọi `StopMorse()`
và tắt `enabled` của từng đèn, rồi bật lại khi puzzle xong.

---

## BƯỚC 6 — Kiểm tra Collider cho raycast

Giống `ComputerInteraction`, `WireBoxInteraction` dùng `Physics.Raycast` từ
camera người chơi, nên:
- GameObject hộp điện **phải có Collider** (Box/Mesh Collider đều được)
- Collider phải nằm trong `interactableLayer` đã chọn ở Bước 5
- Hộp điện **không cần** nằm trong danh sách `collidersToDisable` của
  `ComputerInteraction` (2 minigame độc lập, không tranh collider của nhau)

---

## BƯỚC 7 — Test thử

1. Play scene
2. Bấm E mở **Computer** trước (kiểm tra: KHÔNG mở được vì đã bị khoá —
   nếu vẫn mở được thì kiểm tra lại `Computer Interaction To Lock` đã kéo đúng chưa)
3. Lại gần hộp điện, bấm E → UI nối dây Screen Space hiện ra, cursor mở ra
4. Kéo chuột từ 1 điểm trái sang đúng điểm phải cùng màu → dây nối, cố định
5. Kéo sai màu → dây biến mất (preview bị huỷ), thử lại được
6. Nối đủ 4/4 dây đúng → Console log "HOÀN THÀNH!", UI tự đóng sau 1s
7. Đèn Morse bắt đầu nháy, bấm E ở Computer giờ mở được

---

## Các điểm tuỳ biến nhanh

| Muốn đổi | Sửa ở đâu |
|---|---|
| Số lượng dây (vd 3 hoặc 5 dây) | Thêm/xoá điểm `WireConnectionPoint` trong `WirePanel` + thêm màu vào `wireColors` của `WirePuzzleManager` |
| Vị trí điểm nối | Kéo trực tiếp trong Editor (RectTransform), không cần sửa code |
| Độ dày / màu dây khi vẽ | `wireThickness` và `wireColors` trong `WirePuzzleManager` |
| Thời gian chờ trước khi UI tự đóng sau khi xong | Số `1.0f` trong `Invoke(nameof(ExitWireBox), 1.0f)` ở `WireBoxInteraction` |
| Cho computer minigame **không bị khoá** (đổi ý sau này) | Để trống field `Computer Interaction To Lock` trong Inspector |

---

## Lưu ý kỹ thuật quan trọng

- **Không dùng `EventSystem.RaycastAll` thủ công** như `RaycastDebugger` —
  `WireConnectionPoint` dùng interface `IPointerDownHandler` / `IPointerUpHandler`
  chuẩn của Unity UI, cần Canvas có `GraphicRaycaster` (Canvas Screen Space tự
  có sẵn, không cần thêm gì) và **phải có `EventSystem`** trong scene (thường
  đã có sẵn nếu bạn đã làm computer minigame trước đó).
- `WirePuzzleManager.Update()` chỉ chạy logic vẽ preview khi đang kéo
  (`_dragSource != null`), nên không tốn performance khi không tương tác.
- Dây đã nối đúng (`isConnected = true`) sẽ **không thể kéo lại** — đúng
  hành vi Among Us (nối đúng là xong, không undo).
- `ResetPuzzle()` được gọi mỗi lần mở lại UI (`EnterWireBox()`), nên nếu
  người chơi thoát giữa chừng rồi vào lại, dây nối trước đó sẽ bị xoá hết —
  nếu bạn muốn giữ lại trạng thái cũ, bỏ dòng `puzzleManager.ResetPuzzle();`
  trong `WireBoxInteraction.EnterWireBox()`.
