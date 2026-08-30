# Modbus TCP Read-Only Driver

The `ModbusTcp` driver is included inside the copied SCADA solution. A new SCADA
project is still created by copying the complete `SCADA_TEMPLATE` folder; it has
no source or binary dependency on the original template folder.

## Device configuration

Set `DriverType` to `ModbusTcp` and use these `ConnectionOptions`:

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `Host` | yes | — | DNS name or IP address |
| `Port` | no | `502` | TCP port, 1–65535 |
| `UnitId` | no | `1` | Modbus unit identifier, 0–255 |
| `RegisterByteOrder` | no | `BigEndian` | `BigEndian` or `LittleEndian` inside each 16-bit register |
| `RegisterWordOrder` | no | `HighToLow` | `HighToLow` or `LowToHigh` for multi-register values |

Endpoint configuration belongs in the copied project's `project.json`. Do not
put credentials or customer endpoints into the template source.

## Tag addresses

Addresses are zero-based and explicit:

| Address | Function | Source type |
| --- | --- | --- |
| `C:<offset>` | FC01 Read Coils | `Boolean` |
| `DI:<offset>` | FC02 Read Discrete Inputs | `Boolean` |
| `HR:<offset>:I16` | FC03 Read Holding Registers | `Int32` |
| `HR:<offset>:U16` | FC03 Read Holding Registers | `Int32` |
| `HR:<offset>:I32` | FC03 Read Holding Registers | `Int32` |
| `HR:<offset>:U32` | FC03 Read Holding Registers | `Int64` |
| `HR:<offset>:I64` | FC03 Read Holding Registers | `Int64` |
| `HR:<offset>:F32` | FC03 Read Holding Registers | `Double` |
| `HR:<offset>:F64` | FC03 Read Holding Registers | `Double` |

`IR` supports the same register encodings through FC04. For example,
`IR:20:F32` reads two input registers beginning at zero-based offset 20.

`SourceDataType` must match the type in the table. `DataType`, `Scale` and
`Offset` remain canonical Runtime engineering conversion metadata; the driver
does not apply a second conversion.

## Runtime behavior

- One driver/connection instance is owned per configured device.
- Runtime still schedules one logical device/scan-group batch.
- The driver coalesces contiguous ranges and splits requests at 2,000 bits or
  125 registers.
- Illegal function/address/value responses make only that block `Bad`.
- Connection, timeout, malformed-response and gateway failures use the existing
  device disconnect/reconnect/backoff behavior.
- The driver never performs an extra PLC read for UI, Historian, Alarm or MQTT.

The driver is read-only. FC05, FC06, FC15, FC16 and every PLC/MQTT write path are
outside M17.

## Hardware qualification

Before production deployment, verify a representative PLC/gateway register map,
unit identifier, byte/word order, timeout behavior and disconnect recovery. The
deterministic automated suite proves architecture and protocol mapping but is
not a certificate for every vendor device.
