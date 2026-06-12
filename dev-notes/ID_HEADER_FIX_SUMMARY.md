# ISO15765 ID Header Fix - Implementation Summary

## Problem Identified

The ISO15765 protocol lines contain bracket bytes in the format:
```
[00,00,07,D0,22,91,40]  or  [00,00,07,D8,7F,22,78]
```

Where:
- **First 4 bytes**: ID/Address header (00,00,07,D0 or 00,00,07,D8)
- **Remaining bytes**: UDS payload (22,91,40 or 7F,22,78)

Previously, the UDS decoder was receiving the full byte array including the ID header, so it never detected UDS service IDs (0x22, 0x7F, 0x62) at position [0].

---

## Solution Implemented

### 1. **Iso15765Decoder.cs** - Proper ID Header Separation

**Key Changes:**
```csharp
// Split bracket bytes into ID header (first 4) and UDS payload (remaining)
byte[] idBytes = allBytes.Take(4).ToArray();
byte[] udsPayload = allBytes.Skip(4).ToArray();

// Pass ONLY udsPayload to UdsDecoder (not the full allBytes)
DecodeResult? udsResult = UdsDecoder.TryDecodeFromPayload(udsPayload);
```

**Flow:**
1. Parse bracket bytes into `allBytes[]`
2. If `allBytes.Length <= 4`: Return "ISO15765 metadata only"
3. Extract ID bytes: `idBytes = allBytes[0..3]`
4. Extract UDS payload: `udsPayload = allBytes[4..]`
5. Pass only `udsPayload` to `UdsDecoder.TryDecodeFromPayload()`
6. Include both ID bytes and UDS payload in Details section

**Output Format:**
```
Summary: "ISO15765 TX - UDS Request: ReadDID 0xF188 (Strategy)"
Details:
  Direction: TX
  ID Bytes: [00 00 07 D0]
  UDS Payload: [22 F1 88]
  UDS Request: ReadDataByIdentifier (0x22)
  DID: 0xF188 (Strategy)
```

---

### 2. **UdsDecoder.cs** - Added Request Decoding (0x22)

**New Method: `DecodeReadDidRequest()`**

Handles UDS Request pattern: `0x22 <DID_Hi> <DID_Lo>`

```csharp
// Check for UDS Request: ReadDataByIdentifier (0x22)
if (payload.Length >= 3 && payload[0] == 0x22)
{
    return DecodeReadDidRequest(payload);
}
```

**Decodes:**
- Service ID: 0x22 (ReadDataByIdentifier)
- DID: Combines bytes [1] and [2] into 16-bit DID
- Confidence: 0.9

**Example:**
```
Input: [22, F1, 88]
Output: "UDS Request: ReadDID 0xF188 (Strategy)"
```

---

### 3. **UdsDecoder.cs** - Improved Determinism

**Updated Negative Response** (0x7F):
```csharp
// Deterministic: returns "Unknown" for unknown SID/NRC, not "Unknown(0xXX)"
string serviceName = DecodeTables.UdsServiceNames.TryGetValue(originalSid, out string? svcName)
    ? svcName
    : "Unknown";  // No guessing

string nrcMeaning = DecodeTables.UdsNrcNames.TryGetValue(nrc, out string? nrcName)
    ? nrcName
    : "Unknown";  // No guessing
```

**Updated Positive Response** (0x62):
```csharp
// Deterministic: returns "Unknown" for unknown DID
string didName = DecodeTables.KnownDids.TryGetValue(did, out string? knownName)
    ? knownName
    : "Unknown";  // No guessing
```

---

### 4. **Iso15765Line.cs** - Parallel Implementation

Since the Models project cannot reference Decoders (architectural constraint), `Iso15765Line` implements the same logic:

**Key Method: `DecodeWithIdHeader()`**

```csharp
// Extract ID bytes (first 4 bytes)
byte[] idBytes = _allBytes.Take(4).ToArray();
// Extract UDS payload bytes (everything after first 4 bytes)
byte[] udsPayload = _allBytes.Skip(4).ToArray();
```

**Handles 3 UDS Patterns:**
1. **Request (0x22)**: ReadDataByIdentifier request with DID
2. **Negative Response (0x7F)**: Service ID + NRC
3. **Positive Response (0x62)**: DID + data bytes

---

## Test Cases

### Example 1: UDS Request
```
Input:  "2025-10-21T10:23:47.000 ISO15765 TX -> [00,00,07,D0,22,F1,88]"
Parse:  ID=[00,00,07,D0], UDS=[22,F1,88]
Decode: 0x22 = ReadDataByIdentifier, DID=0xF188
Output: "ISO15765 TX - UDS Request: ReadDID 0xF188 (Strategy)"
```

### Example 2: UDS Negative Response
```
Input:  "2025-10-21T10:23:45.123 ISO15765 RX <- [00,00,07,D8,7F,22,78]"
Parse:  ID=[00,00,07,D8], UDS=[7F,22,78]
Decode: 0x7F, OrigSID=0x22 (ReadDataByIdentifier), NRC=0x78 (ResponsePending)
Output: "ISO15765 RX - UDS Negative Response: ReadDataByIdentifier (0x22) NRC ResponsePending (0x78)"
```

### Example 3: UDS Positive Response
```
Input:  "2025-10-21T10:23:47.100 ISO15765 RX <- [00,00,07,D8,62,F1,88,56,45,52,53,49,4F,4E,31]"
Parse:  ID=[00,00,07,D8], UDS=[62,F1,88,56,45,52,53,49,4F,4E,31]
Decode: 0x62, DID=0xF188, Data=[56,45,52,53,49,4F,4E,31]
Output: "ISO15765 RX - UDS Positive Response (ReadDID): DID 0xF188 (Strategy)"
        Data (ASCII): "VERSION1"
```

### Example 4: ID Only (No UDS Payload)
```
Input:  "[00,00,07,D0]"
Parse:  allBytes.Length = 4
Output: "ISO15765 Unknown (ID only, no UDS payload)"
```

---

## Deterministic Behavior

✅ **No Guessing**: Unknown DIDs/NRCs/Services labeled as "Unknown" (not "Unknown(0xXX)")  
✅ **Consistent**: Same input always produces same output  
✅ **Confidence Levels**:
  - 0x22 Request: 0.9
  - 0x7F Negative Response: 1.0
  - 0x62 Positive Response: 0.9
  - No UDS decode: 0.6
  - ID only: 0.5
  - Metadata only: 0.4

---

## Files Modified

1. **Iso15765Decoder.cs** - Split ID header from UDS payload, updated output format
2. **UdsDecoder.cs** - Added 0x22 request handling, made all decoding deterministic
3. **Iso15765Line.cs** - Parallel implementation with ID header separation

---

## Build Status

✅ **Build Successful**  
✅ **No Breaking Changes**  
✅ **All Comments Added**  
✅ **Deterministic Decoding**  
✅ **Proper OOP Separation** (Models independent of Decoders)

---

## Usage

The sample data now correctly decodes:

```csharp
// Sample lines in Form1.cs already include proper ISO15765 examples:
"2025-10-21T10:23:47.000 ISO15765 TX -> [00,00,07,D0,22,F1,88]"  // Request
"2025-10-21T10:23:45.123 ISO15765 RX <- [00,00,07,D8,7F,22,78]"  // Negative Response
"2025-10-21T10:23:47.100 ISO15765 RX <- [00,00,07,D8,62,F1,88,...]"  // Positive Response
```

Run the app and click **"Load Sample"** to see the improved UDS decoding with proper ID header handling!
