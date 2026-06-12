# Multi-Keyword Search Implementation Summary

## Overview
Added advanced multi-keyword search functionality to the AutoDecoder WinForms application with support for quoted phrases, AND/OR logic, and live filtering.

## New Features

### 1. Multi-Keyword Search
- **Search multiple keywords** separated by spaces
- **Quoted phrase support** - Use double quotes to search for exact phrases (e.g., `"negative response"`)
- **Case-insensitive** matching across all searchable fields

### 2. AND/OR Search Logic
- **New checkbox**: "Match all terms" (default: checked)
  - **Checked (AND)**: All search terms must be present
  - **Unchecked (OR)**: Any search term present matches
- Toggle between AND/OR logic in real-time

### 3. Enhanced Search Scope
Search now covers a **combined field** including:
- `Raw` - Original log line text
- `Summary` - Line type summary
- `Details` - Decoded details

### 4. Live Filtering
- Search filters update **immediately** as you type (TextChanged event)
- No need to press Enter or click a button

### 5. Null Safety
- All string operations handle null values safely
- No crashes from missing data

## Code Changes

### Form1.cs

#### New Helper Method: `TokenizeSearch()`
```csharp
private static List<string> TokenizeSearch(string input)
```
**Purpose**: Parse search input into tokens while respecting quoted phrases

**Features**:
- Splits by spaces except inside double quotes
- Handles quoted phrases as single tokens
- Trims whitespace from tokens
- Filters out empty tokens
- Returns empty list for null/whitespace input

**Examples**:
```
Input: "0x7F response"
Output: ["0x7F", "response"]

Input: "negative response" UDS
Output: ["negative response", "UDS"]

Input: ISO 0x62 "positive response"
Output: ["ISO", "0x62", "positive response"]
```

#### Updated Method: `ApplyFilters()`
**Changes**:
1. Tokenizes search text using `TokenizeSearch()`
2. Builds combined search field from `Raw + Summary + Details`
3. Implements AND/OR logic based on `chkMatchAllTerms.Checked`
4. Uses LINQ `.All()` for AND logic (all tokens must match)
5. Uses LINQ `.Any()` for OR logic (any token matches)
6. Maintains existing Type and UDS filters

**Search Logic**:
```csharp
// AND logic - all tokens must be present
matchesSearch = searchTokens.All(token => 
    searchField.Contains(token.ToLower(), StringComparison.OrdinalIgnoreCase));

// OR logic - any token present
matchesSearch = searchTokens.Any(token => 
    searchField.Contains(token.ToLower(), StringComparison.OrdinalIgnoreCase));
```

### Form1.Designer.cs

#### New Control: `chkMatchAllTerms`
- **Type**: CheckBox
- **Text**: "Match all terms"
- **Location**: Between search box and Type dropdown
- **Default State**: Checked (AND search)
- **Event**: `CheckedChanged` → `FilterControls_Changed`

#### Updated Layout
```
[Search: [___________] [✓] Match all terms  Type: [___▼___] [✓] Only UDS Findings]
   65px      200px      275px     130px      415px   120px    590px     150px
```

#### Event Wiring
- `txtSearch.TextChanged` → `FilterControls_Changed` (live filtering)
- `chkMatchAllTerms.CheckedChanged` → `FilterControls_Changed`

## Usage Examples

### Example 1: Find All Negative Responses
**Search**: `0x7F`
**Match all terms**: ✓ (checked)
**Result**: All lines containing "0x7F"

### Example 2: Find Negative Response with Specific NRC
**Search**: `0x7F 0x22`
**Match all terms**: ✓ (checked)
**Result**: Lines containing BOTH "0x7F" AND "0x22"

### Example 3: Find Exact Phrase
**Search**: `"negative response"`
**Match all terms**: ✓ (checked)
**Result**: Lines containing the exact phrase "negative response"

### Example 4: Find Lines with Any Keyword (OR Search)
**Search**: `0x7F 0x62 0x3E`
**Match all terms**: ☐ (unchecked)
**Result**: Lines containing "0x7F" OR "0x62" OR "0x3E"

### Example 5: Complex Multi-Term AND Search
**Search**: `ISO "UDS Request" 0x22`
**Match all terms**: ✓ (checked)
**Result**: Lines containing "ISO" AND "UDS Request" AND "0x22"

### Example 6: Complex Multi-Term OR Search
**Search**: `"positive response" "negative response"`
**Match all terms**: ☐ (unchecked)
**Result**: Lines containing either "positive response" OR "negative response"

## Search Token Examples

| Input | Parsed Tokens |
|-------|---------------|
| `0x7F` | `["0x7F"]` |
| `0x7F 0x22` | `["0x7F", "0x22"]` |
| `"negative response"` | `["negative response"]` |
| `ISO "UDS Request" TX` | `["ISO", "UDS Request", "TX"]` |
| `  multiple   spaces  ` | `["multiple", "spaces"]` |
| `""` | `[]` (empty) |

## Filter Combination

All filters work together in the following order:
1. **Search Filter** (multi-keyword with AND/OR)
2. **Type Filter** (Iso15765, Xml, Hex, etc.)
3. **UDS Filter** (Only UDS Findings checkbox)

All filters must pass for a line to be displayed.

## Technical Details

### Null Safety
```csharp
// Safe string operations
string combinedField = (logLine.Raw ?? string.Empty) + " " +
                      (logLine.Summary ?? string.Empty) + " " +
                      (logLine.Details ?? string.Empty);
```

### Case-Insensitive Matching
```csharp
// All comparisons use OrdinalIgnoreCase
searchField.Contains(token.ToLower(), StringComparison.OrdinalIgnoreCase)
```

### Live Filtering
```csharp
// TextChanged event triggers immediate filtering
txtSearch.TextChanged += FilterControls_Changed;
```

## Performance Considerations

- Tokenization is efficient (single pass through input string)
- Search builds combined field once per line
- LINQ `.All()` and `.Any()` short-circuit on first match/mismatch
- Case conversion happens once per search operation

## UI Layout Updates

**Filter Panel Adjustments**:
- Search textbox remains at 200px width
- New checkbox added at position 275px
- Type label moved to 415px (from 275px)
- Type dropdown moved to 460px (from 320px)
- UDS checkbox moved to 590px (from 450px)

**Total filter panel width**: ~750px (accommodates new control)

## Backward Compatibility

✅ **Fully backward compatible**
- Empty search returns all lines (no filtering)
- Single keyword works as before (case-insensitive contains)
- Existing Type and UDS filters unchanged
- Default state (AND search) provides most intuitive behavior

## Build Status
✅ **Build successful** - All changes compile without errors

## Testing Recommendations

1. **Basic Search**: Test single keyword (e.g., `0x7F`)
2. **Multi-Keyword AND**: Test multiple keywords with checkbox checked
3. **Multi-Keyword OR**: Test multiple keywords with checkbox unchecked
4. **Quoted Phrases**: Test `"negative response"` and `"positive response"`
5. **Combined Filters**: Test search + Type filter + UDS filter together
6. **Edge Cases**: Empty search, quoted empty string, multiple spaces
7. **Live Filtering**: Verify filtering updates as you type
8. **Null Handling**: Test on log lines with missing Raw/Summary/Details
