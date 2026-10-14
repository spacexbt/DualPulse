# Protocol notes

These notes describe how DualPulse talks to each device family. Every packet listed here was verified against real hardware.

## Windows Dynamic Lighting (HID LampArray, usage page `0x59`)

This is the standard from the HID Usage Tables. Report IDs and field layout differ between devices, so DualPulse never
hardcodes offsets. It reads the feature value caps through the Windows HID parser (`HidP_GetValueCaps`) and groups
them by their link-collection usage:

| Collection | Usage | Used for |
|---|---|---|
| `0x02` LampArrayAttributesReport | `0x03` LampCount, `0x04/0x05` bounding box (µm), `0x07` kind, `0x08` min update interval (µs) | discovery |
| `0x20` LampAttributesRequestReport | `0x21` LampId | select a lamp to describe |
| `0x22` LampAttributesResponseReport | `0x23/0x24` position X/Y (µm), `0x28` red level count | lamp layout |
| `0x50` LampMultiUpdateReport | `0x03` count, `0x55` flags, `0x21` lamp ids, `0x51-0x54` R/G/B/intensity | per-lamp colors |
| `0x60` LampRangeUpdateReport | `0x55` flags, `0x61/0x62` start/end, `0x51-0x54` color | uniform color |
| `0x70` LampArrayControlReport | `0x71` AutonomousMode | take / release control |

Some devices declare the R/G/B/I channels of a multi-update as an **array** (one value cap, `ReportCount = N`).
Others repeat the usage N times as separate caps. DualPulse handles both cases: arrays go through
`HidP_SetUsageValueArray`, and repeated caps are addressed by `DataIndex` with `HidP_SetData`.
The last report of a frame sets `LampUpdateComplete` (flag `0x01`).

## Logitech HID++ 2.0

Uses the vendor collection `FF00` / usage `0002` (20-byte long reports, report ID `0x11`).
The device index is `0xFF` for wired devices and `1-6` for devices paired to a receiver.

```
request:  11 <index> <featureIndex> <function<<4 | swId> <params...>
error:    11 <index> FF <featureIndex> <fn|sw> <errorCode>
```

The root feature (index 0) maps a feature ID to its index: `getFeature(idHi, idLo)`. Features used:

| Feature | Function | Notes |
|---|---|---|
| `0x0005` Device name & type | 0 `getCount`, 1 `getName(offset)`, 2 `getType` | type 0 keyboard, 3 mouse, 8 headset… |
| `0x8070` Color LED effects | 0 `getInfo` (zone count), 1 `getZoneInfo` (location), 3 `setZoneEffect` | params `zone, effect, r, g, b, …, persist`. DualPulse writes `persist = 0` (RAM only) so streaming never touches flash. |
| `0x2201` Adjustable DPI | 1 `getDpiList`, 2 `getDpi`, 3 `setDpi(sensor, hi, lo)` | the list is either a range `min, 0xE000 \| step, max` or explicit values |
| `0x8060` Report rate | 0 `getList` (bitmask of ms), 1 `get`, 2 `set(ms)` | |
| `0x8100` Onboard profiles | 1 `setMode` (1 onboard, 2 host), 2 `getMode` | lighting and DPI changes need host mode |

## Corsair protocol v2

Uses the vendor collection `FF42` (64-byte reports plus report ID 0). Wired devices prefix commands with `0x08`.
Responses echo the command, with a status byte at offset 3 (`0` = OK).

```
GET   08 02 <prop> 00                 -> 00 02 00 <value LE>
SET   08 01 <prop> 00 <value LE>
OPEN  08 0D 00 <endpoint>             endpoint 0x01 = lighting (software mode only)
WRITE 08 06 00 <len u32 LE> <data>    first block
      08 07 00 <data>                 continuation blocks
```

| Property | Meaning |
|---|---|
| `0x01` | polling rate: 1 = 125 Hz, 2 = 250, 3 = 500, 4 = 1000 … |
| `0x02` | brightness 0-1000 |
| `0x03` | mode: 1 hardware (onboard), 2 software (host) |
| `0x11` / `0x12` | VID / PID (used to confirm the protocol) |

Lighting data is `12 00` followed by all red values, then all green, then all blue (planar), one byte per LED
index. The K70 CORE RGB has 123 LED slots. The key-to-LED map follows HID usage minus 4 for regular keys, with
modifiers after the `0x65` block.
