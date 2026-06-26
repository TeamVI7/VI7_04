# Hướng dẫn cài đặt hệ thống Thang máy (chọn tầng + khóa thẻ + màn hình chỉ hướng)

Bộ script gồm 9 file:
1. `ElevatorFloorData.cs` — dữ liệu 1 tầng (không tự gắn vào object nào)
2. `PlayerCardHolder.cs` — gắn vào **Player**
3. `ElevatorController.cs` — gắn vào object **gốc của cabin thang máy**
4. `ElevatorDoor.cs` — gắn vào **2 cánh cửa** (trái/phải)
5. `ElevatorProximityZone.cs` — gắn vào **vùng cảm ứng ngoài cửa**
6. `ElevatorFloorSelectionUI.cs` — gắn vào **panel chọn tầng** (Canvas trong cabin)
7. `ElevatorDisplayUI.cs` — gắn vào **màn hình hiển thị** (Canvas nhỏ phía trên cửa)
8. `ElevatorFloorButtonLookable.cs` — gắn vào **mỗi nút chọn tầng** (trong prefab nút)
9. `PlayerElevatorInteractor.cs` — gắn vào **Player hoặc Camera của Player**

> **Cơ chế chọn tầng:** không dùng chuột click nút nữa. Player **nhìn vào nút** (camera raycast trúng nút) → nút **sáng lên**, nhấn phím **E** để xác nhận chọn.

Thứ tự cài đặt nên làm theo đúng thứ tự dưới đây.

---

## 1. `PlayerCardHolder.cs` — gắn vào Player

**Cách gắn:** Chọn object Player trong Hierarchy → kéo script vào, hoặc Add Component → tìm "PlayerCardHolder".

**Cách dùng:**
- Trường `ownedCardIds` là danh sách string (mã thẻ). Có thể điền sẵn trên Inspector để test (VD: kéo size = 1, nhập `the_ky_thuat`), hoặc để trống và gọi bằng code khi người chơi nhặt thẻ trong game:

```csharp
GetComponent<PlayerCardHolder>().AddCard("the_ky_thuat");
```

- Mã thẻ là **chuỗi tự do**, bạn tự đặt tên (ví dụ `"the_quan_ly"`, `"the_bao_ve"`, `"the_tang_3"`...). Quan trọng là mã này phải **khớp chính xác (phân biệt hoa thường)** với mã đã điền ở `requiredCardId` của tầng tương ứng trong `ElevatorController`.
- Nếu game có nhiều màn/scene, nhớ đừng để object Player bị destroy khi chuyển scene (hoặc lưu danh sách thẻ vào hệ thống save) nếu muốn giữ thẻ qua nhiều màn.

**Lưu ý:** Script này không có gì để "bật/tắt", chỉ là nơi chứa dữ liệu. Không cần kéo gì vào Inspector của chính nó.

---

## 2. `ElevatorFloorData.cs` — không gắn vào object, chỉ là dữ liệu

Đây không phải là component, không tự gắn vào object nào. Nó là cấu trúc dữ liệu hiện ra **bên trong Inspector của `ElevatorController`**, trong danh sách `floors`. Xem mục 3 để biết cách điền.

---

## 3. `ElevatorController.cs` — gắn vào object gốc của cabin

**Cách gắn:** Object cabin (object chứa Collider để phát hiện Player đứng trong thang) → Add Component → `ElevatorController`.

> Object này cần có một **Collider dạng Trigger** (BoxCollider, tích `Is Trigger`) bao trùm không gian bên trong cabin — đây là nơi script dùng `OnTriggerEnter/OnTriggerExit` để biết Player đã vào trong cabin hay chưa.

### Cài Inspector:

**Danh sách tầng (`floors`):**
- Đây là phần quan trọng nhất. Bấm vào mũi tên mở rộng, sửa `Size` = số tầng bạn muốn (VD 4 tầng → Size = 4).
- Với mỗi tầng (Element 0, 1, 2...), điền:
  | Trường | Ý nghĩa | Ví dụ |
  |---|---|---|
  | `floorName` | Tên hiển thị trên nút chọn tầng | `"Tầng 2"`, `"Hầm B1"` |
  | `displayNumber` | Số hiện trên màn hình LED trong cabin | `2`, `-1` |
  | `floorY` | **Tọa độ Y thế giới** mà cabin sẽ di chuyển tới khi dừng ở tầng này — đo trong scene bằng cách kéo cabin tới đúng độ cao của tầng đó rồi xem giá trị Y trên Transform | `0`, `4.5`, `-5` |
  | `isLocked` | Tích nếu tầng này cần thẻ mới vào được | ✅ / ❌ |
  | `requiredCardId` | Mã thẻ cần có (phải khớp với mã trong `PlayerCardHolder` của Player). Để trống = tầng khóa nhưng ai cũng vào được (coi như chưa cấu hình thẻ) | `"the_ky_thuat"` |

  > **Mẹo đo `floorY`:** Tạm thời kéo object cabin trong Scene view lên đúng vị trí từng tầng (canh theo sàn nhà, khung cửa...), copy giá trị Y, rồi dán vào `floorY` của tầng đó. Sau khi đo xong tất cả các tầng, để cabin về lại vị trí tầng đầu (tầng index 0).

**Cửa (`doorLeft`, `doorRight`):** kéo 2 object cánh cửa (đã gắn `ElevatorDoor.cs`, xem mục 4) vào 2 ô này.

**Hint UI (`hintInsideUI`):** (tùy chọn) một GameObject chứa chữ gợi ý kiểu "Chọn tầng để đi" — tự ẩn/hiện khi đang ở trạng thái chờ chọn tầng và Player đang trong cabin. Có thể bỏ trống nếu không cần.

**Cài đặt:**
- `moveSpeed`: tốc độ cabin di chuyển (đơn vị/giây).
- `doorOpenWaitTime`: số giây cửa mở chờ Player chọn tầng trước khi tự đóng lại nếu Player rời khỏi cabin.

**Âm thanh:** kéo AudioClip cho `moveSound` (tiếng máy chạy khi di chuyển), `arriveSound` (tiếng "ting" khi đến tầng), `deniedSound` (tiếng báo lỗi khi chọn tầng bị khóa mà thiếu thẻ). `audioSource` để trống cũng được — script tự thêm `AudioSource` nếu chưa có.

### Cách hoạt động (để bạn test đúng luồng):
1. Player bước vào vùng cảm ứng ngoài cửa (`ElevatorProximityZone`) → cửa tự mở.
2. Cửa mở hết → vào trạng thái **chờ chọn tầng**, panel chọn tầng hiện ra (nếu Player đang đứng trong cabin).
3. Nếu không chọn gì trong `doorOpenWaitTime` giây và Player đã ra khỏi cabin → cửa tự đóng, về Idle.
4. Nếu Player chọn 1 tầng hợp lệ (không khóa, hoặc có đủ thẻ) → cửa đóng → cabin di chuyển tới tầng đó → tới nơi tự mở cửa.
5. Nếu chọn tầng bị khóa mà không đủ thẻ → bị từ chối, phát `deniedSound`, panel vẫn mở để chọn lại.

---

## 4. `ElevatorDoor.cs` — gắn vào từng cánh cửa (không đổi so với bản cũ)

**Cách gắn:** Gắn riêng vào object cánh cửa trái và object cánh cửa phải (2 object khác nhau, 2 component khác nhau).

- `side`: chọn `Left` hoặc `Right` tương ứng — quyết định cửa trượt sang hướng nào khi mở.
- `openDistance`: khoảng cách trượt ra khi mở (mét).
- `speed`: tốc độ trượt cửa.
- `openSound` / `closeSound`: âm thanh khi mở/đóng (tùy chọn).

Không cần chỉnh gì thêm — `ElevatorController` sẽ tự gọi `Open()`/`Close()` của 2 cửa này.

---

## 5. `ElevatorProximityZone.cs` — gắn vào vùng cảm ứng ngoài cửa (không đổi)

**Cách gắn:** Tạo 1 object con (Empty GameObject) đặt **ngay ngoài cửa thang máy** (khu vực hành lang trước cửa), thêm Collider dạng Trigger, gắn script này.

- Script tự tìm `ElevatorController` ở **object cha** bằng `GetComponentInParent`. Vì vậy object này phải là **con của** (hoặc cùng cây với) object có `ElevatorController`. Nếu để sai vị trí trong Hierarchy, sẽ báo lỗi đỏ trong Console lúc Play.
- Không cần kéo gì vào Inspector — không có trường nào để điền.

---

## 6. `ElevatorFloorSelectionUI.cs` — panel chọn tầng trong cabin

**Quan trọng:** Canvas chọn tầng bắt buộc là **World Space** (không dùng Screen Space Overlay), vì nút được chọn bằng raycast 3D từ camera Player chứ không phải click chuột trên màn hình.

**Chuẩn bị trước khi gắn script:**

1. Tạo 1 **Canvas** → đổi `Render Mode` = **World Space**. Đặt áp vào mặt tường trong cabin (giống bảng điều khiển thang máy thật), kéo `Scale` nhỏ lại (vd `0.01, 0.01, 0.01`) cho vừa kích thước thật.
2. Trong Canvas, tạo:
   - 1 object **Panel** (đây là `panelRoot` — toàn bộ khung chọn tầng, tự ẩn/hiện).
   - Trong Panel, tạo 1 object trống làm **container** (gắn `Vertical Layout Group` hoặc `Grid Layout Group`) — đây là `buttonContainer`.
3. Tạo **1 prefab nút** mẫu — xem chi tiết các component cần có ở **mục 8** ngay dưới đây.

**Gắn script `ElevatorFloorSelectionUI` vào object Panel** (hoặc Canvas), rồi điền Inspector:
| Trường | Gán gì |
|---|---|
| `elevator` | kéo object cabin (có `ElevatorController`) vào |
| `panelRoot` | kéo object Panel vào |
| `buttonContainer` | kéo object container (có Layout Group) vào |
| `buttonPrefab` | kéo prefab nút đã tạo ở mục 8 vào |

**Cách hoạt động:** Lúc `Start()`, script tự sinh ra đúng số nút bằng số tầng trong `floors`, đặt tên nút theo `floorName`, tự gán `floorIndex`/`elevator` cho mỗi nút. Panel tự ẩn/hiện theo trạng thái thang máy. Việc *chọn* nút (nhìn vào + nhấn E) do `PlayerElevatorInteractor` (mục 9) xử lý — script này không còn dùng `Button.onClick`.

> Nếu dùng TextMeshPro thay vì Text thường: đổi `using UnityEngine.UI;` → `using TMPro;` và đổi kiểu `Text` → `TMP_Text`, báo mình nếu cần mình sửa sẵn.

---

## 7. `ElevatorDisplayUI.cs` — màn hình hiển thị mũi tên + số tầng

**Chuẩn bị:**
1. Tạo 1 Canvas nhỏ (đặt phía trên cửa hoặc góc cabin, mô phỏng màn hình LED thang máy thật).
2. Trong Canvas, tạo:
   - 1 `Image` mũi tên chỉ lên (`arrowUp`) — để icon mũi tên ▲, mặc định tắt (SetActive(false)).
   - 1 `Image` mũi tên chỉ xuống (`arrowDown`) — icon mũi tên ▼, mặc định tắt.
   - 1 `Text` hiển thị số tầng (`floorNumberText`).

**Gắn script vào Canvas này**, điền Inspector:
| Trường | Gán gì |
|---|---|
| `elevator` | kéo object cabin (có `ElevatorController`) vào |
| `arrowUp` | object Image mũi tên lên |
| `arrowDown` | object Image mũi tên xuống |
| `floorNumberText` | object Text số tầng |

**Cách hoạt động:** Tự động — không cần code thêm. Khi cabin bắt đầu di chuyển, mũi tên tương ứng hiện lên và số tầng cập nhật liên tục theo vị trí thực tế của cabin (đi qua tầng nào, hiện số tầng đó), tới đích thì tắt mũi tên và hiện số tầng cuối cùng.

---

## 8. `ElevatorFloorButtonLookable.cs` — gắn vào prefab nút chọn tầng

Đây là script quyết định 1 nút **có sáng lên được khi bị nhìn vào hay không**.

**Cách dựng prefab nút (làm trong Scene, sau đó kéo ra Prefab):**

1. Tạo 1 `Image` (UI) làm nền nút — đây sẽ là object gốc của prefab.
2. Tạo con: 1 `Text` đặt tên chính xác **"Label"** — hiển thị tên tầng.
3. Tạo con: 1 `Image` đặt tên chính xác **"LockIcon"** — gán sprite hình khóa, **tắt sẵn** (uncheck active) — script tự bật khi tầng bị khóa.
4. Add Component **`Box Collider`** vào object gốc (nền nút):
   - Vì Canvas là World Space, kích thước Collider tính theo đơn vị world. Chỉnh `Size` của Box Collider sao cho vừa khớp khung nút nhìn trong Scene view (bật Gizmo để thấy khung xanh của Collider, kéo chỉnh cho trùng với khung nút).
   - Không cần tích `Is Trigger` (raycast vẫn trúng collider thường).
5. Add Component **`ElevatorFloorButtonLookable`** vào object gốc, điền:
   | Trường | Gán gì |
   |---|---|
   | `targetGraphic` | kéo chính `Image` nền nút (object gốc) vào |
   | `normalColor` | màu nút lúc bình thường (vd trắng) |
   | `highlightColor` | màu nút lúc được nhìn vào (vd vàng) |
6. Kéo object này ra Project thành Prefab, xóa bản trong Scene.

> Các trường `floorIndex` và `elevator` **không cần điền tay** — `ElevatorFloorSelectionUI` tự gán khi sinh nút lúc Play.

---

## 9. `PlayerElevatorInteractor.cs` — gắn vào Player hoặc Camera

Script này làm nhiệm vụ "con mắt" của Player: mỗi frame bắn 1 tia theo hướng camera nhìn, nếu trúng nút thì làm nút sáng lên; nhấn **E** thì xác nhận chọn.

**Cách gắn:** Gắn vào object Player, hoặc gắn trực tiếp vào Camera (camera chính của Player) — đều được, miễn camera nhìn đúng hướng người chơi đang nhìn.

**Điền Inspector:**
| Trường | Ý nghĩa | Gợi ý |
|---|---|---|
| `playerCamera` | Camera dùng để bắn tia | Để trống script tự lấy `Camera.main` |
| `interactDistance` | Khoảng cách tối đa nhìn trúng nút | `3` (mét) — chỉnh theo kích thước cabin |
| `interactKey` | Phím xác nhận chọn | `E` (mặc định) |
| `interactLayerMask` | Lọc layer raycast quét tới | Để mặc định (quét tất cả) nếu không có nhiều object chắn; nếu muốn tối ưu, tạo riêng 1 Layer (vd `"Interactable"`) gán cho prefab nút và chọn layer đó ở đây |
| `interactHintUI` | (tùy chọn) Object hiện chữ "Nhấn E để chọn" khi đang nhìn vào nút hợp lệ | Tạo 1 Text nhỏ giữa màn hình (kiểu crosshair hint), kéo vào đây |

**Lưu ý quan trọng:**
- Camera phải có **đường thẳng không bị vật cản** tới nút (nếu tường/object khác chắn giữa Player và nút, sẽ không trúng raycast — đúng như hành vi nhìn thật).
- Nếu Player đứng quá xa nút (`interactDistance` quá nhỏ) thì dù nhìn đúng hướng cũng không sáng — tăng giá trị này nếu cabin rộng.
- Nên có 1 **crosshair/dấu chấm giữa màn hình** (UI riêng, không thuộc bộ script này) để Player biết mình đang "nhìn" vào đâu — đây là làm thêm tùy game, không bắt buộc để hệ thống chạy đúng.

---

## Thứ tự setup nhanh (checklist)

- [ ] Gắn `PlayerCardHolder` vào Player, điền sẵn 1–2 mã thẻ để test.
- [ ] Gắn `ElevatorDoor` vào 2 cánh cửa, chọn đúng `side`.
- [ ] Gắn `ElevatorProximityZone` vào vùng cảm ứng ngoài cửa (đặt làm con của cabin).
- [ ] Gắn `ElevatorController` vào cabin, đo và điền `floorY` cho từng tầng trong `floors`, đặt `isLocked` + `requiredCardId` cho tầng cần khóa, kéo 2 cửa vào.
- [ ] Làm prefab nút có `ElevatorFloorButtonLookable` + `Box Collider` (mục 8), dựng Canvas World Space chọn tầng, gắn `ElevatorFloorSelectionUI`.
- [ ] Gắn `PlayerElevatorInteractor` vào Player/Camera (mục 9).
- [ ] Dựng Canvas màn hình, gắn `ElevatorDisplayUI`.
- [ ] Play thử: đi vào vùng cảm ứng → cửa mở → panel chọn tầng hiện → **nhìn vào 1 nút** (nút sáng lên) → **nhấn E** → thử chọn tầng khóa khi chưa có thẻ (bị từ chối), rồi thêm thẻ vào `PlayerCardHolder` để thử lại → cửa đóng → cabin di chuyển, màn hình hiện đúng mũi tên + số tầng → tới tầng đích, cửa mở lại.

## Lỗi thường gặp

| Lỗi                                              | Nguyên nhân thường gặp |
|---|---|
| Cabin không nhận biết Player vào trong           | Thiếu Collider `Is Trigger` trên object cabin, hoặc Player thiếu tag `"Player"` |
| Panel chọn tầng không hiện                       | Chưa gán đúng `panelRoot`, hoặc Player chưa thực sự đứng trong cabin khi cửa mở |
| Nhìn vào nút mà không sáng lên                   | Canvas chưa đổi sang **World Space**, hoặc nút thiếu `Box Collider`, hoặc Collider quá nhỏ/lệch vị trí, hoặc `interactDistance` quá ngắn |
| Nhìn sáng nút nhưng nhấn E không chọn được       | Chưa gắn `PlayerElevatorInteractor` vào Player/Camera, hoặc `playerCamera` đang trỏ sai camera |
| Tên object con trong prefab không đúng           | Tên object con trong prefab nút không đúng `"Label"`/`"LockIcon"`, hoặc chưa gán `elevator` cho `ElevatorFloorSelectionUI` |
| Luôn bị từ chối dù đã có thẻ                     | Mã thẻ trong `requiredCardId` và trong `PlayerCardHolder.ownedCardIds` không khớp tuyệt đối (sai hoa/thường hoặc dư khoảng trắng) |
| Cabin di chuyển sai vị trí                       | `floorY` đo sai — nhớ đo theo tọa độ Y **thế giới** của cabin, không phải tọa độ local |
