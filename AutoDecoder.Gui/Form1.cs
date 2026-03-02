// ================================================================
// File: Form1.cs
// Project: AutoDecoder.Gui
// Author: Harold L.R. Watkins
//
// PRODUCT:
//   AutoDecoder Workbench
//
// HIGH-LEVEL PURPOSE (what this executable does):
//   1) Accept diagnostic logs (load from file OR paste from clipboard)
//   2) Convert each raw text line into a typed object (LogLine / Iso15765Line / XmlLine / UnknownLine, etc.)
//   3) Let each object decode itself (ParseAndDecode = encapsulation of decoding logic)
//   4) Display results in a professional WinForms UI (grid + raw/decoded panes)
//   5) Provide engineering filters (search tokens, type filter, UDS-only)
//   6) Provide engineering summaries (NRC/DID frequency + conversation counts)
//
// ARCHITECTURE (how the solution is split):
//   - AutoDecoder.Gui (THIS FILE): UI layout + event handlers + data binding
//   - AutoDecoder.Models: strongly-typed line objects (LogLine etc.) that hold parsed/decoded fields
//   - AutoDecoder.Protocols: parsing/decoding engines (ISO-TP reassembly, UDS conversations, tables)
//   - AutoDecoder.Protocols.Reference: static reference tables (SID names, NRC meanings, DID labels)
//   - Optional CSV: NodeAddress.csv maps CAN IDs -> human friendly module name
//
// CAPSTONE REQUIREMENTS (what this project demonstrates):
//   - Variables/operators/expressions: counters, booleans, string building, math/clamping, LINQ counts
//   - Procedures + control structures: loops, foreach, if/else, try/catch, event handlers
//   - Numeric/alphanumeric/array types: ints/bytes/ushorts, strings, string[], lists, dictionaries
//   - GUI with multiple components: SplitContainers, TabControl, DataGridView, ListView, RichTextBox, etc.
//   - Multiple forms: AboutForm + Form1
//
// STABILITY / EXECUTION NOTES (rubric: “never terminates prematurely”):
//   - All external I/O (file read, CSV load, decode) is wrapped with try/catch
//   - SplitContainer distances are applied only AFTER handles exist (OnShown + BeginInvoke)
//   - SafeSetSplitterDistance clamps values to avoid InvalidOperationException
// ================================================================

#nullable enable

// ----------------------------
// Project references (your own DLL projects)
// ----------------------------
using AutoDecoder.Models;                     // LogLine, LogSession, UnknownLine, etc.
using AutoDecoder.Protocols.Classifiers;      // LineClassifier: "what kind of line is this?"
using AutoDecoder.Protocols.Conversations;    // ISO-TP reassembly + UDS conversation builder
using AutoDecoder.Protocols.Utilities;        // UdsTables and general helpers

// IMPORTANT: Fix ambiguity when two namespaces have a class with the same name.
// This alias guarantees we ALWAYS call the Protocols address book (the one that has AddOrUpdate).
using ProtocolModuleAddressBook = AutoDecoder.Protocols.Utilities.ModuleAddressBook;

// ----------------------------
// .NET framework references
// ----------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;                 // BindingList<T> (WinForms data-binding friendly list)
using System.Drawing;                        // Color, Font, Point, Size
using System.Globalization;                  // Hex parsing with invariant culture
using System.IO;                             // File IO
using System.Linq;                           // LINQ counts, projections, ordering
using System.Windows.Forms;                  // WinForms controls

namespace AutoDecoder.Gui
{
    public class Form1 : Form
    {
        // ================================================================
        // CONSTANTS / APP IDENTITY
        // ================================================================

        // Single source of truth for the main window title
        private const string AppTitle = "AutoDecoder Workbench";

        // ================================================================
        // DERIVED PROTOCOL ARTIFACTS (outputs of deeper decoding)
        // ================================================================

        // ISO-TP PDUs assembled from raw ISO15765 frames
        // (These are derived AFTER we load and classify lines.)
        private List<IsoTpPdu> _pdus = new();

        // UDS transactions reconstructed from PDUs
        // (Used for the “conversation count” and future conversation-level views.)
        private List<UdsTransaction> _transactions = new();

        // ================================================================
        // MAIN LAYOUT SPLIT CONTAINERS (we store as fields so we can safely set distances later)
        // ================================================================

        // Top-level left/right split (sessions list on left, tabs on right)
        private SplitContainer? splitMain;

        // Decoded tab: top controls vs bottom content
        private SplitContainer? decodedRootSplit;

        // Decoded tab bottom: grid vs details pane
        private SplitContainer? decodedBottomSplit;

        // Details pane: raw vs decoded
        private SplitContainer? rawDecodedSplit;

        // Reference tab splitters (kept as fields so we can set splitter distances safely in OnShown)
        private SplitContainer? referenceRootSplit;
        private SplitContainer? referenceTopSplit;

        // ================================================================
        // SESSION MANAGEMENT (multiple “workspaces” inside one running app)
        // ================================================================

        // Requirement: keep sessions limited to protect UI performance
        private const int MaxSessions = 5;

        // BindingList works well with WinForms controls (auto refresh)
        private readonly BindingList<LogSession> _sessions = new();

        // The session currently shown in the UI
        private LogSession? _activeSession;

        // ================================================================
        // DATA COLLECTIONS FOR CURRENT SESSION (all vs filtered)
        // ================================================================

        // All loaded lines (one object per raw line)
        private BindingList<LogLine> _allLogLines = new();

        // Filtered view shown in the DataGridView
        private BindingList<LogLine> _filteredLogLines = new();

        // ================================================================
        // LEFT PANEL CONTROLS (session UI)
        // ================================================================

        private ListBox? lstSessions;
        private Button? btnAddSession;
        private Button? btnCloseSession;

        // ================================================================
        // TABS
        // ================================================================

        private TabControl? tabControl;
        private TabPage? tabDecoded;
        private TabPage? tabSummary;
        private TabPage? tabReference;

        // ================================================================
        // DECODED TAB CONTROLS (inputs + filters)
        // ================================================================

        private Button? btnLoadFile;
        private Button? btnLoadSample;
        private Button? btnPaste;
        private Button? btnClear;

        private TextBox? txtSearch;
        private ComboBox? cboTypeFilter;
        private CheckBox? chkUdsOnly;
        private CheckBox? chkMatchAllTerms;

        // Status line counts displayed under the buttons
        private Label? lblStatusTotal;
        private Label? lblStatusIso;
        private Label? lblStatusXml;
        private Label? lblStatusUnknown;

        // ================================================================
        // GRID + DETAILS PANE
        // ================================================================

        private DataGridView? dgvLines;
        private RichTextBox? rtbRaw;
        private RichTextBox? rtbDecoded;

        // ================================================================
        // SUMMARY TAB CONTROLS
        // ================================================================

        private Label? lblSummaryIso;
        private Label? lblSummaryUds;
        private Label? lblSummaryUnknown;
        private ListView? lvNrc;
        private ListView? lvDid;

        // ================================================================
        // INTERNAL FLAGS / CHROME
        // ================================================================

        // Guard to ensure we only hook grid binding once
        private bool _gridBindingHooked;

        // Menu strip + container that prevents menu overlap
        private MenuStrip? _menu;
        private ToolStripContainer? _chrome;

        // ================================================================
        // CONSTRUCTOR (startup path)
        // ================================================================

        public Form1()
        {
            // 1) Build all UI components (controls/splits/tabs)
            BuildUi();

            // 2) Connect event handlers (button clicks, text change, selection change)
            WireEvents();

            // 3) Always start with at least one session ready to use
            CreateNewSession(makeActive: true);

            // 4) Attempt to load optional CAN node mapping from CSV
            //    If it fails, the app continues to run (rubric: no premature termination)
            TryLoadNodeAddressBook();
        }

        // ================================================================
        // MENU (secondary form entry point)
        // ================================================================

        private MenuStrip BuildMenu()
        {
            // Create the strip itself
            var menu = new MenuStrip();

            // Create a single menu item that opens AboutForm (requirement: multiple forms)
            var aboutMenu = new ToolStripMenuItem("About");

            // When user clicks "About", show the second form as modal
            aboutMenu.Click += (_, __) => new AboutForm().ShowDialog(this);

            // Add menu item to the strip
            menu.Items.Add(aboutMenu);

            // Return the built menu strip to caller (BuildUi)
            return menu;
        }

        // ================================================================
        // NODE ADDRESS BOOK (optional CSV mapping CAN ID -> module name)
        // ================================================================

        private void TryLoadNodeAddressBook()
        {
            // WHY TRY/CATCH?
            // CSV is optional. If missing or malformed, we display an info box and keep running.
            try
            {
                // BaseDirectory points to the built output folder (bin\Debug...\ or bin\Release...\)
                // So you can ship NodeAddress.csv next to the executable.
                var csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NodeAddress.csv");

                // Actual parser that reads and imports CSV data
                LoadNodeAddressCsv(csvPath);
            }
            catch (Exception ex)
            {
                // We do NOT crash the application.
                // We simply inform the user and continue.
                MessageBox.Show(
                    "Optional NodeAddress.csv could not be loaded.\n\n" +
                    "The app will still run, but node names may be limited.\n\n" +
                    "Details: " + ex.Message,
                    "Node Address Book",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }

        private void LoadNodeAddressCsv(string path)
        {
            // If the file is not present, throw a clear exception (caught above)
            if (!File.Exists(path))
                throw new FileNotFoundException("NodeAddress.csv not found.", path);

            // Read entire file as lines (simple and fine for this use case)
            var lines = File.ReadAllLines(path);

            // Skip header row (index 0) and iterate through each mapping row
            foreach (var line in lines.Skip(1))
            {
                // Ignore empty rows
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Expected: CAN_ID_HEX, ABBREV, NAME
                var parts = line.Split(',');

                // If the row does not have expected columns, skip it safely
                if (parts.Length < 3)
                    continue;

                // Extract columns with trim (defensive against spacing)
                var canIdHex = parts[0].Trim();  // e.g. 0x7D0 or 7D0
                var abbrev = parts[1].Trim();    // e.g. APIM
                var name = parts[2].Trim();      // e.g. Accessory Protocol Interface Module

                // Normalize by removing "0x" prefix if present
                var hex = canIdHex.Replace("0x", "", StringComparison.OrdinalIgnoreCase);

                // Convert from hex string to integer CAN ID
                // (Numeric types requirement: int parsing with NumberStyles.HexNumber)
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int canId))
                {
                    // Store it into the Protocols address book (alias prevents ambiguity)
                    ProtocolModuleAddressBook.AddOrUpdate(canId, abbrev, name);
                }
            }
        }

        // ================================================================
        // UI BUILD (creates all WinForms controls)
        // ================================================================

        private void BuildUi()
        {
            // Basic window identity + initial size
            Text = AppTitle;
            Width = 1400;
            Height = 850;
            StartPosition = FormStartPosition.CenterScreen;

            // Suspend layout while we create a lot of controls (reduces flicker + speeds build)
            SuspendLayout();

            // ----------------------------
            // ToolStripContainer:
            //   TopToolStripPanel = menu area
            //   ContentPanel = app content area
            // This avoids menu overlap issues on resize.
            // ----------------------------
            _chrome = new ToolStripContainer
            {
                Dock = DockStyle.Fill
            };

            // ----------------------------
            // Menu strip creation + placement
            // ----------------------------
            _menu = BuildMenu();
            MainMenuStrip = _menu;

            // IMPORTANT: Don’t Dock menu when inside ToolStripContainer
            _menu.Dock = DockStyle.None;
            _chrome.TopToolStripPanel.Controls.Clear();
            _chrome.TopToolStripPanel.Controls.Add(_menu);

            // ----------------------------
            // Main split container
            // Left = sessions, Right = tabs
            // NOTE: We intentionally do NOT set SplitterDistance here.
            // We wait until OnShown so handles exist and SplitterDistance is safe.
            // ----------------------------
            splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1
            };

            _chrome.ContentPanel.Controls.Clear();
            _chrome.ContentPanel.Controls.Add(splitMain);

            // Put chrome container on the form
            Controls.Clear();
            Controls.Add(_chrome);

            // ----------------------------
            // Left panel: Sessions
            // ----------------------------
            var leftHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            // Top-down button stack (New Session, Close Session)
            var leftButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            btnAddSession = new Button
            {
                Text = "New Session",
                Width = 140,
                Height = 32,
                Margin = new Padding(0, 0, 0, 6)
            };

            btnCloseSession = new Button
            {
                Text = "Close Session",
                Width = 140,
                Height = 32,
                Margin = new Padding(0)
            };

            leftButtons.Controls.Add(btnAddSession);
            leftButtons.Controls.Add(btnCloseSession);

            // Session list box (shows session names)
            lstSessions = new ListBox
            {
                Dock = DockStyle.Top,
                IntegralHeight = true,
                DisplayMember = "Name",
                Margin = new Padding(0, 8, 0, 0)
            };

            // Keep the list box height reasonable (MaxSessions visible)
            lstSessions.Height = (lstSessions.ItemHeight * MaxSessions) + 6;

            // Filler panel takes remaining left space
            var leftFiller = new Panel { Dock = DockStyle.Fill };

            // Order matters: filler at bottom, then list, then buttons at top
            leftHost.Controls.Add(leftFiller);
            leftHost.Controls.Add(lstSessions);
            leftHost.Controls.Add(leftButtons);

            splitMain.Panel1.Controls.Clear();
            splitMain.Panel1.Controls.Add(leftHost);

            // ----------------------------
            // Right panel: TabControl
            // ----------------------------
            tabControl = new TabControl { Dock = DockStyle.Fill };

            // Three tabs: Decoded, Summary, Reference
            tabDecoded = new TabPage("Decoded");
            tabSummary = new TabPage("Summary");
            tabReference = new TabPage("Reference");

            tabControl.TabPages.Add(tabDecoded);
            tabControl.TabPages.Add(tabSummary);
            tabControl.TabPages.Add(tabReference);

            splitMain.Panel2.Controls.Clear();
            splitMain.Panel2.Controls.Add(tabControl);

            // Build each tab’s UI subtree
            BuildDecodedTab();
            BuildSummaryTab();
            BuildReferenceTab(tabReference);

            // Resume normal layout processing
            ResumeLayout(true);
        }

        // ================================================================
        // DECODED TAB (primary workflow screen)
        // ================================================================

        private void BuildDecodedTab()
        {
            // Guard: if tab wasn’t created, exit safely
            if (tabDecoded == null) return;

            // Clear in case this is rebuilt later
            tabDecoded.Controls.Clear();

            // Root split: Top = toolbar area, Bottom = grid/details
            decodedRootSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 6
            };
            tabDecoded.Controls.Add(decodedRootSplit);

            // ----------------------------
            // Top controls layout:
            // TableLayoutPanel gives stable column sizing for buttons/filters/status
            // ----------------------------
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 10,
                RowCount = 2,
                Padding = new Padding(8),
                Margin = new Padding(0)
            };

            // Column sizes: fixed for buttons/labels, percent for search box
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));   // Load File
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));   // Load Sample
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));   // Paste
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));   // Clear
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));   // "Search:"
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // Search textbox
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));   // "Type:"
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));  // Type dropdown
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));   // UDS only
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));  // Match all terms

            // Two rows: controls + status line
            top.RowStyles.Clear();
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            // ----------------------------
            // Action buttons (control structures requirement: event handlers below)
            // ----------------------------
            btnLoadFile = new Button { Text = "Load File", Dock = DockStyle.Fill, Margin = new Padding(2) };
            btnLoadSample = new Button { Text = "Load Sample", Dock = DockStyle.Fill, Margin = new Padding(2) };
            btnPaste = new Button { Text = "Paste", Dock = DockStyle.Fill, Margin = new Padding(2) };
            btnClear = new Button { Text = "Clear", Dock = DockStyle.Fill, Margin = new Padding(2) };

            // Search label
            var lblSearch = new Label
            {
                Text = "Search:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 2, 0)
            };

            // Search input box
            txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 5, 2, 2)
            };

            // Type label
            var lblType = new Label
            {
                Text = "Type:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 2, 0)
            };

            // Type dropdown (All + enum values)
            cboTypeFilter = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(2, 5, 2, 2)
            };

            // Quick toggle filters
            chkUdsOnly = new CheckBox { Text = "UDS only", Dock = DockStyle.Fill, Margin = new Padding(8, 6, 2, 2) };
            chkMatchAllTerms = new CheckBox { Text = "Match all terms", Dock = DockStyle.Fill, Margin = new Padding(8, 6, 2, 2) };

            // Populate type filter options
            cboTypeFilter.Items.Add("All");
            foreach (var v in Enum.GetValues(typeof(LineType)).Cast<LineType>())
                cboTypeFilter.Items.Add(v.ToString());
            cboTypeFilter.SelectedItem = "All";

            // Add row 0 controls in the expected columns
            top.Controls.Add(btnLoadFile, 0, 0);
            top.Controls.Add(btnLoadSample, 1, 0);
            top.Controls.Add(btnPaste, 2, 0);
            top.Controls.Add(btnClear, 3, 0);
            top.Controls.Add(lblSearch, 4, 0);
            top.Controls.Add(txtSearch, 5, 0);
            top.Controls.Add(lblType, 6, 0);
            top.Controls.Add(cboTypeFilter, 7, 0);
            top.Controls.Add(chkUdsOnly, 8, 0);
            top.Controls.Add(chkMatchAllTerms, 9, 0);

            // ----------------------------
            // Status bar line (counts by type)
            // ----------------------------
            var statusPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(2, 0, 2, 0)
            };

            lblStatusTotal = new Label { AutoSize = true, Text = "Total: 0", Padding = new Padding(0, 4, 15, 0) };
            lblStatusIso = new Label { AutoSize = true, Text = "ISO: 0", Padding = new Padding(0, 4, 15, 0) };
            lblStatusXml = new Label { AutoSize = true, Text = "XML: 0", Padding = new Padding(0, 4, 15, 0) };
            lblStatusUnknown = new Label { AutoSize = true, Text = "Unknown: 0", Padding = new Padding(0, 4, 15, 0) };

            statusPanel.Controls.Add(lblStatusTotal);
            statusPanel.Controls.Add(lblStatusIso);
            statusPanel.Controls.Add(lblStatusXml);
            statusPanel.Controls.Add(lblStatusUnknown);

            // Put status panel on row 1 spanning all columns
            top.Controls.Add(statusPanel, 0, 1);
            top.SetColumnSpan(statusPanel, 10);

            // Place top area into the fixed top panel of the root split
            decodedRootSplit.Panel1.Controls.Clear();
            decodedRootSplit.Panel1.Controls.Add(top);

            // ----------------------------
            // Bottom split: grid (top) + detail panes (bottom)
            // ----------------------------
            decodedBottomSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6
            };

            decodedRootSplit.Panel2.Controls.Clear();
            decodedRootSplit.Panel2.Controls.Add(decodedBottomSplit);

            // ----------------------------
            // Data grid: filtered lines only
            // ----------------------------
            dgvLines = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = true
            };

            // Hook DataBindingComplete once (so we can remove/resize columns safely)
            HookGridBindingOnce();

            decodedBottomSplit.Panel1.Controls.Clear();
            decodedBottomSplit.Panel1.Controls.Add(dgvLines);

            // ----------------------------
            // Details split: raw (left) and decoded (right)
            // ----------------------------
            rawDecodedSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6
            };

            decodedBottomSplit.Panel2.Controls.Clear();
            decodedBottomSplit.Panel2.Controls.Add(rawDecodedSplit);

            // Raw view (what the log line looked like)
            rtbRaw = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };

            // Decoded view (engineering interpretation / breakdown)
            rtbDecoded = new RichTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };

            rawDecodedSplit.Panel1.Controls.Clear();
            rawDecodedSplit.Panel2.Controls.Clear();
            rawDecodedSplit.Panel1.Controls.Add(rtbRaw);
            rawDecodedSplit.Panel2.Controls.Add(rtbDecoded);

            // Bind grid to the filtered list (this is what user sees)
            dgvLines.DataSource = _filteredLogLines;

            // Apply initial grid behavior settings
            ConfigureDataGridColumns();
        }

        // ================================================================
        // REFERENCE TAB (engineering lookup tables)
        // ================================================================

        private void BuildReferenceTab(TabPage tabRef)
        {
            // Clear old controls (if any)
            tabRef.Controls.Clear();

            // Root split: top area (UDS + NRC) and bottom area (DIDs)
            referenceRootSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6
            };

            // Top split: left = UDS, right = NRC
            referenceTopSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6
            };

            // UDS services grid
            var grdUds = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
            };

            // NRC meanings grid
            var grdNrc = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
            };

            // Data binding: project your dictionaries into simple rows for DataGridView
            grdUds.DataSource = AutoDecoder.Protocols.Reference.UdsServiceTable.RequestSidToName
                .Select(kvp => new { SID = $"0x{kvp.Key:X2}", Name = kvp.Value })
                .ToList();

            grdNrc.DataSource = AutoDecoder.Protocols.Reference.NrcTable.CodeToMeaning
                .Select(kvp => new { NRC = $"0x{kvp.Key:X2}", Meaning = kvp.Value })
                .ToList();

            // Place UDS and NRC grids into the top split panels
            referenceTopSplit.Panel1.Controls.Clear();
            referenceTopSplit.Panel2.Controls.Clear();
            referenceTopSplit.Panel1.Controls.Add(grdUds);
            referenceTopSplit.Panel2.Controls.Add(grdNrc);

            // DID grid placeholder (you can bind the DID table here if desired)
            var grdDid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
            };

            // Assemble referenceRootSplit panels
            referenceRootSplit.Panel1.Controls.Clear();
            referenceRootSplit.Panel2.Controls.Clear();
            referenceRootSplit.Panel1.Controls.Add(referenceTopSplit);
            referenceRootSplit.Panel2.Controls.Add(grdDid);

            // Add the root split to the tab
            tabRef.Controls.Add(referenceRootSplit);
        }

        // ================================================================
        // SUMMARY TAB (counts + drill-down navigation)
        // ================================================================

        private void BuildSummaryTab()
        {
            // Guard
            if (tabSummary == null) return;

            // Table layout: top summary row + 2 list views below
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(8)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            layout.RowStyles.Clear();
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            tabSummary.Controls.Clear();
            tabSummary.Controls.Add(layout);

            // Top row: quick counters
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            lblSummaryIso = new Label { AutoSize = true, Text = "ISO Lines: 0", Padding = new Padding(0, 10, 20, 0) };
            lblSummaryUds = new Label { AutoSize = true, Text = "UDS Findings: 0", Padding = new Padding(0, 10, 20, 0) };
            lblSummaryUnknown = new Label { AutoSize = true, Text = "Unknown: 0", Padding = new Padding(0, 10, 20, 0) };

            top.Controls.Add(lblSummaryIso);
            top.Controls.Add(lblSummaryUds);
            top.Controls.Add(lblSummaryUnknown);

            layout.Controls.Add(top, 0, 0);
            layout.SetColumnSpan(top, 2);

            // NRC list (left)
            lvNrc = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lvNrc.Columns.Add("NRC", 90);
            lvNrc.Columns.Add("Meaning", 260);
            lvNrc.Columns.Add("Count", 80);

            // DID list (right)
            lvDid = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true
            };
            lvDid.Columns.Add("DID", 90);
            lvDid.Columns.Add("Name", 260);
            lvDid.Columns.Add("Count", 80);

            // Group boxes provide a clean professional header around each list
            var gbNrc = new GroupBox { Text = "NRCs", Dock = DockStyle.Fill };
            gbNrc.Controls.Add(lvNrc);

            var gbDid = new GroupBox { Text = "DIDs", Dock = DockStyle.Fill };
            gbDid.Controls.Add(lvDid);

            layout.Controls.Add(gbNrc, 0, 1);
            layout.Controls.Add(gbDid, 1, 1);
        }

        // ================================================================
        // EVENT WIRING (connect UI actions to logic)
        // ================================================================

        private void WireEvents()
        {
            // Buttons -> load/paste/clear actions
            btnLoadFile!.Click += BtnLoadFile_Click;
            btnLoadSample!.Click += BtnLoadSample_Click;
            btnPaste!.Click += BtnPaste_Click;
            btnClear!.Click += BtnClear_Click;

            // Grid rendering + selection updates
            dgvLines!.RowPrePaint += DgvLines_RowPrePaint;
            dgvLines.SelectionChanged += DgvLines_SelectionChanged;

            // Filter inputs -> re-apply filters on any change
            txtSearch!.TextChanged += FilterControls_Changed;
            cboTypeFilter!.SelectedIndexChanged += FilterControls_Changed;
            chkUdsOnly!.CheckedChanged += FilterControls_Changed;
            chkMatchAllTerms!.CheckedChanged += FilterControls_Changed;

            // Summary list activation -> jump back to decoded tab and set search token
            lvNrc!.ItemActivate += LvNrc_ItemActivate;
            lvDid!.ItemActivate += LvDid_ItemActivate;

            // Session controls
            btnAddSession!.Click += BtnAddSession_Click;
            btnCloseSession!.Click += BtnCloseSession_Click;
            lstSessions!.SelectedIndexChanged += LstSessions_SelectedIndexChanged;
        }

        // ================================================================
        // SESSIONS (create / close / switch)
        // ================================================================

        private void CreateNewSession(bool makeActive)
        {
            // Performance guard: enforce max sessions
            if (_sessions.Count >= MaxSessions)
            {
                MessageBox.Show($"Max sessions reached ({MaxSessions}).", "Sessions",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Create a session object (object creation requirement)
            var s = new LogSession
            {
                Name = $"Session {_sessions.Count + 1} - {DateTime.Now:HH:mm:ss}"
            };

            // Add to list (BindingList will notify UI)
            _sessions.Add(s);

            // First-time hookup for the ListBox data source
            if (lstSessions!.DataSource == null)
                lstSessions.DataSource = _sessions;

            // Optionally activate immediately
            if (makeActive)
            {
                lstSessions.SelectedItem = s;
                SetActiveSession(s);
            }
        }

        private void SetActiveSession(LogSession? session)
        {
            // Update which session the UI is pointing at
            _activeSession = session;

            // If null, reset everything safely (no crashes)
            if (_activeSession == null)
            {
                _allLogLines = new BindingList<LogLine>();
                _filteredLogLines = new BindingList<LogLine>();
                dgvLines!.DataSource = _filteredLogLines;

                UpdateStatusBar();
                UpdateFindingsSummary();
                return;
            }

            // Point our local lists at the session’s lists (shared state per session)
            _allLogLines = _activeSession.AllLines;
            _filteredLogLines = _activeSession.FilteredLines;

            // Rebind the grid to the filtered list
            dgvLines!.DataSource = _filteredLogLines;

            // Re-apply filters and refresh summary counts
            ApplyFilters();
            UpdateStatusBar();
            UpdateFindingsSummary();
        }

        // Simple event handler wrapper: new session
        private void BtnAddSession_Click(object? sender, EventArgs e) => CreateNewSession(makeActive: true);

        private void BtnCloseSession_Click(object? sender, EventArgs e)
        {
            // If nothing active, nothing to close
            if (_activeSession == null) return;

            // Remember the index so we can select a nearby session afterward
            int idx = lstSessions!.SelectedIndex;

            // Remove the active session from the list
            var toClose = _activeSession;
            _sessions.Remove(toClose);

            // If we removed the last session, create a fresh one
            if (_sessions.Count == 0)
            {
                CreateNewSession(makeActive: true);
                return;
            }

            // Select the next session (clamped)
            int nextIdx = Math.Min(idx, _sessions.Count - 1);
            lstSessions.SelectedIndex = nextIdx;

            // Activate the newly selected session
            SetActiveSession(lstSessions.SelectedItem as LogSession);
        }

        // Whenever the list selection changes, switch active session
        private void LstSessions_SelectedIndexChanged(object? sender, EventArgs e)
            => SetActiveSession(lstSessions!.SelectedItem as LogSession);

        // ================================================================
        // GRID CONFIGURATION (appearance, columns, binding hooks)
        // ================================================================

        private void ConfigureDataGridColumns()
        {
            // We want manual control over widths for a stable “tool” feel
            dgvLines!.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            // Allow engineers to resize and reorder as needed
            dgvLines.AllowUserToResizeColumns = true;
            dgvLines.AllowUserToOrderColumns = true;
        }

        private void HookGridBindingOnce()
        {
            // Prevent double-hooking event handlers
            if (_gridBindingHooked) return;
            _gridBindingHooked = true;

            // DataBindingComplete fires after the grid generates columns from the bound objects
            dgvLines!.DataBindingComplete += (_, __) =>
            {
                // Hide/remove fields that are not meant for end-user display
                RemoveColumnIfExists(dgvLines, "Confidence");
                RemoveColumnIfExists(dgvLines, "CanId");
                RemoveColumnIfExists(dgvLines, "Timestamp");
                RemoveColumnIfExists(dgvLines, "TimestampText");

                RemoveColumnIfExists(dgvLines, "Did");
                RemoveColumnIfExists(dgvLines, "UdsDid");
                RemoveColumnIfExists(dgvLines, "UdsSid");
                RemoveColumnIfExists(dgvLines, "UdsNrc");

                // Ensure node column is visible if it exists
                if (dgvLines.Columns.Contains("CanNode"))
                {
                    var col = dgvLines.Columns["CanNode"];
                    if (col != null) col.Visible = true;
                }

                // Apply consistent widths and ordering after columns exist
                ApplyColumnSizing();
                ApplySafeDisplayOrder();
            };
        }

        private void ApplySafeDisplayOrder()
        {
            // Defensive checks (rubric: don’t crash)
            if (dgvLines!.IsDisposed) return;
            if (dgvLines.Columns == null || dgvLines.Columns.Count == 0) return;

            // Display order we want for the “Decoded” table
            string[] desired =
            {
                "LineNumber",
                "Raw",
                "Type",
                "Summary",
                "Details",
                "CanNode"
            };

            // Build list of columns that exist
            var cols = desired
                .Where(n => dgvLines.Columns.Contains(n))
                .Select(n => dgvLines.Columns[n])
                .Where(c => c != null)
                .ToList();

            // Set display order left-to-right
            for (int i = 0; i < cols.Count; i++)
                cols[i]!.DisplayIndex = i;
        }

        private static void RemoveColumnIfExists(DataGridView? grid, string columnName)
        {
            // Defensive
            if (grid == null) return;
            if (grid.Columns == null) return;

            // Remove if present
            if (grid.Columns.Contains(columnName))
                grid.Columns.Remove(columnName);
        }

        private void ApplyColumnSizing()
        {
            // Defensive
            if (dgvLines!.IsDisposed) return;
            if (dgvLines.Columns == null || dgvLines.Columns.Count == 0) return;

            // Manual sizing mode
            dgvLines.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            // Helper local function: set width + header label in one place
            void SetCol(string name, int width, string? header = null)
            {
                if (!dgvLines.Columns.Contains(name)) return;

                var col = dgvLines.Columns[name];
                if (col == null) return;

                col.Visible = true;
                col.Width = width;
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                col.Resizable = DataGridViewTriState.True;

                if (!string.IsNullOrWhiteSpace(header))
                    col.HeaderText = header;
            }

            // Column widths tuned for “triage tool” readability
            SetCol("LineNumber", 80, "Line");
            SetCol("Raw", 360, "Raw");
            SetCol("Type", 95, "Type");
            SetCol("Summary", 320, "Report Summary");
            SetCol("Details", 520, "Technical Breakdown");
            SetCol("CanNode", 170, "Node");

            // Keep details as single-line visual to avoid row height explosions
            if (dgvLines.Columns.Contains("Details"))
            {
                var c = dgvLines.Columns["Details"];
                if (c != null) c.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            }
        }

        // ================================================================
        // LOAD / PASTE / CLEAR ACTIONS (main data entry points)
        // ================================================================

        private void BtnLoadFile_Click(object? sender, EventArgs e)
        {
            // File picker dialog
            using var ofd = new OpenFileDialog
            {
                Title = "Select Log File",
                Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*"
            };

            // If user cancels, do nothing
            if (ofd.ShowDialog() != DialogResult.OK) return;

            // Try loading the file safely
            try
            {
                var lines = File.ReadAllLines(ofd.FileName);
                string fileName = Path.GetFileName(ofd.FileName);

                // Pipeline: load -> classify -> decode -> display
                LoadLines(lines, sessionName: fileName);
            }
            catch (Exception ex)
            {
                // Never crash due to file I/O
                MessageBox.Show($"Error loading file: {ex.Message}", "Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnLoadSample_Click(object? sender, EventArgs e)
        {
            // A small built-in sample set for demos and quick testing
            // (Array types requirement: string[])
            string[] sampleLines =
            {
                "ISO15765 TX -> [00,00,07,E0,10,03]",
                "ISO15765 RX <- [00,00,07,E8,50,03]",
                "ISO15765 TX -> [00,00,07,E0,22,F1,99]",
                "ISO15765 RX <- [00,00,07,E8,62,F1,99,44,41]",
                "ISO15765 RX <- [00,00,07,E8,7F,22,13]",
                "ISO15765 TX -> [00,00,07,E0,3E,00]",
                "ISO15765 RX <- [00,00,07,E8,7E,00]",
                "DEBUG: Starting diagnostic session",
                "<ns3:didValue didValue=\"F188\" type=\"Strategy\"><ns3:Response>4D59535452415445475931</ns3:Response></ns3:didValue>",
            };

            // Load the sample into current session
            LoadLines(sampleLines, sessionName: "Sample");
        }

        private void BtnClear_Click(object? sender, EventArgs e)
        {
            // Clear all data objects from current session
            _allLogLines.Clear();
            _filteredLogLines.Clear();

            // Clear UI panes
            rtbRaw!.Clear();
            rtbDecoded!.Clear();

            // Refresh counters
            UpdateStatusBar();
            UpdateFindingsSummary();
        }

        private void BtnPaste_Click(object? sender, EventArgs e)
        {
            // Guard: clipboard must contain text
            if (!Clipboard.ContainsText())
            {
                MessageBox.Show("Clipboard does not contain text.", "Paste",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Read clipboard
            var text = Clipboard.GetText();

            // Guard: non-empty text
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Clipboard text is empty.", "Paste",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Split into lines using common newline patterns
            // (Control structures requirement: splitting and calling pipeline)
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Pipeline: load these lines
            LoadLines(lines, sessionName: "Pasted");
        }

        // ================================================================
        // ROW SELECTION + VISUAL HIGHLIGHTING
        // ================================================================

        private void DgvLines_SelectionChanged(object? sender, EventArgs e)
        {
            // Guard: must have a selected row
            if (dgvLines!.SelectedRows.Count <= 0) return;

            // Get the first selected row
            var row = dgvLines.SelectedRows[0];

            // DataBoundItem is the object from BindingList<LogLine>
            if (row.DataBoundItem is not LogLine logLine) return;

            // Populate raw and decoded panes
            rtbRaw!.Text = logLine.Raw ?? string.Empty;
            rtbDecoded!.Text = logLine.Details ?? string.Empty;
        }

        private void DgvLines_RowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
        {
            // Defensive checks
            if (e.RowIndex < 0 || e.RowIndex >= dgvLines!.Rows.Count) return;

            var row = dgvLines.Rows[e.RowIndex];
            var logLine = row.DataBoundItem as LogLine;

            // Default if binding is not ready
            if (logLine == null)
            {
                row.DefaultCellStyle.BackColor = Color.White;
                return;
            }

            // Highlight patterns only for ISO lines (UDS traffic lives there)
            if (logLine.Type == LineType.Iso15765)
            {
                // Negative responses (UDS 0x7F) = “problem / error” -> salmon
                if (logLine.Details?.Contains("Negative Response", StringComparison.OrdinalIgnoreCase) == true ||
                    logLine.Details?.Contains("0x7F", StringComparison.OrdinalIgnoreCase) == true)
                {
                    row.DefaultCellStyle.BackColor = Color.LightSalmon;
                    return;
                }

                // Requests -> blue
                if (logLine.Details?.Contains("UDS Request", StringComparison.OrdinalIgnoreCase) == true)
                {
                    row.DefaultCellStyle.BackColor = Color.LightSkyBlue;
                    return;
                }

                // Positive responses -> green
                if (logLine.Details?.Contains("UDS Positive Response", StringComparison.OrdinalIgnoreCase) == true ||
                    logLine.Details?.Contains("(0x62)", StringComparison.OrdinalIgnoreCase) == true)
                {
                    row.DefaultCellStyle.BackColor = Color.LightGreen;
                    return;
                }
            }

            // Default background if no highlight conditions matched
            row.DefaultCellStyle.BackColor = Color.White;
        }

        // ================================================================
        // FILTERS (search + type + UDS-only)
        // ================================================================

        private void FilterControls_Changed(object? sender, EventArgs e) => ApplyFilters();

        private static List<string> TokenizeSearch(string input)
        {
            // Tokenization supports:
            //   - normal space-separated tokens
            //   - quoted phrases (e.g. "negative response")
            var tokens = new List<string>();

            // If empty search, return no tokens (means “match everything”)
            if (string.IsNullOrWhiteSpace(input)) return tokens;

            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in input)
            {
                // Toggle quote mode (do not include quotes in token)
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                // Space ends token ONLY when not inside quotes
                if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString().Trim());
                        current.Clear();
                    }
                }
                else
                {
                    // Append normal character to current token
                    current.Append(c);
                }
            }

            // Add final token if any
            if (current.Length > 0)
                tokens.Add(current.ToString().Trim());

            // Return only non-empty tokens
            return tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        private static string NormalizeForSearch(string s)
        {
            // Normalize to improve matching:
            //   - lowercase
            //   - replace punctuation with spaces
            //   - collapse multiple spaces to single spaces
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var chars = s.ToLowerInvariant().ToCharArray();

            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];

                // Replace punctuation that commonly appears in logs
                if (c is ',' or '[' or ']' or '(' or ')' or '{' or '}' or ':' or ';' or '\t')
                    chars[i] = ' ';
            }

            // Collapse whitespace
            return string.Join(" ", new string(chars)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ApplyFilters()
        {
            // Read UI filter controls
            string searchText = (txtSearch!.Text ?? string.Empty).Trim();
            var tokens = TokenizeSearch(searchText);

            bool matchAll = chkMatchAllTerms!.Checked;
            string typeFilter = cboTypeFilter!.SelectedItem?.ToString() ?? "All";
            bool udsOnly = chkUdsOnly!.Checked;

            // Rebuild filtered list from scratch (simple, predictable behavior)
            _filteredLogLines.Clear();

            // Loop through all lines and decide if each one passes filters
            foreach (var logLine in _allLogLines)
            {
                // Build a single searchable string from raw + summary + details
                string combined =
                    (logLine.Raw ?? "") + " " +
                    (logLine.Summary ?? "") + " " +
                    (logLine.Details ?? "");

                // Normalize once per line (performance + consistent search)
                string field = NormalizeForSearch(combined);

                // 1) SEARCH MATCH: tokens must match All or Any
                bool matchesSearch = true;
                if (tokens.Count > 0)
                {
                    matchesSearch = matchAll
                        ? tokens.All(t => field.Contains(NormalizeForSearch(t)))
                        : tokens.Any(t => field.Contains(NormalizeForSearch(t)));
                }

                // 2) TYPE MATCH: either “All” or exact match to the line’s type
                bool matchesType = typeFilter == "All" || logLine.Type.ToString() == typeFilter;

                // 3) UDS-ONLY: keep line only if details mention UDS
                bool matchesUds =
                    !udsOnly ||
                    (logLine.Details?.Contains("UDS", StringComparison.OrdinalIgnoreCase) == true);

                // If it passes all enabled filters, include it
                if (matchesSearch && matchesType && matchesUds)
                    _filteredLogLines.Add(logLine);
            }

            // Whenever filter changes, update summary counts to match the filtered view
            UpdateFindingsSummary();
        }

        // ================================================================
        // LOAD PIPELINE (raw lines -> objects -> decoded -> summaries)
        // ================================================================

        private void LoadLines(string[] lines, string? sessionName = null)
        {
            // Ensure we have an active session available
            if (_activeSession == null)
                CreateNewSession(makeActive: true);

            // If a session name was provided (file name, “Sample”, “Pasted”), rename session
            if (_activeSession != null && !string.IsNullOrWhiteSpace(sessionName))
            {
                _activeSession.Name = sessionName;
                RefreshSessionListUi();
            }

            // Full pipeline in try/catch so bad input never crashes app
            try
            {
                // Reset collections
                _allLogLines.Clear();
                _filteredLogLines.Clear();

                // Iterate through every raw line and build a LogLine object
                for (int i = 0; i < lines.Length; i++)
                {
                    string rawLine = lines[i];
                    int lineNumber = i + 1;

                    try
                    {
                        // 1) CLASSIFY: choose derived type (Iso15765Line / XmlLine / UnknownLine / etc.)
                        var logLine = LineClassifier.Classify(lineNumber, rawLine);

                        // 2) DECODE: each object decodes itself (encapsulation)
                        logLine.ParseAndDecode();

                        // 3) STORE: add to the all-lines list
                        _allLogLines.Add(logLine);
                    }
                    catch (Exception ex)
                    {
                        // If one line fails, we do NOT kill the whole load.
                        // We record it as an UnknownLine with an error detail.
                        var errorLine = new UnknownLine(lineNumber, rawLine, $"Error: {ex.Message}");
                        errorLine.ParseAndDecode();
                        _allLogLines.Add(errorLine);
                    }
                }

                // Apply filters so the grid shows data immediately
                ApplyFilters();

                // Build deeper artifacts from the full line list (not just filtered)
                _pdus = IsoTpReassembler.Build(_allLogLines);
                _transactions = UdsConversationBuilder.Build(_pdus);

                // Refresh status counters + summary tab counts
                UpdateStatusBar();
                UpdateFindingsSummary();
            }
            catch (Exception ex)
            {
                // Catch any unexpected issue and keep app alive
                MessageBox.Show($"Error loading lines: {ex.Message}", "Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================================================================
        // STATUS + SUMMARY UPDATES
        // ================================================================

        private void UpdateStatusBar()
        {
            // Count by line type (numeric requirement + LINQ)
            int total = _allLogLines.Count;
            int iso = _allLogLines.Count(l => l.Type == LineType.Iso15765);
            int xml = _allLogLines.Count(l => l.Type == LineType.Xml);
            int unk = _allLogLines.Count(l => l.Type == LineType.Unknown);

            // Update UI labels
            lblStatusTotal!.Text = $"Total: {total}";
            lblStatusIso!.Text = $"ISO: {iso}";
            lblStatusXml!.Text = $"XML: {xml}";
            lblStatusUnknown!.Text = $"Unknown: {unk}";
        }

        private void UpdateFindingsSummary()
        {
            // Build aggregated findings for the CURRENT FILTERED VIEW
            var summary = FindingsAggregator.Build(_filteredLogLines);

            // Top summary labels
            lblSummaryIso!.Text = $"ISO Lines: {summary.IsoLines}";
            lblSummaryUds!.Text = $"UDS Findings: {summary.UdsFindingLines}";
            lblSummaryUnknown!.Text = $"Unknown: {summary.UnknownLines}";

            // Populate list views with counts
            PopulateNrcListView(summary.NrcCounts);
            PopulateDidListView(summary.DidCounts);

            // Add conversation count from reconstructed UDS transactions
            lblSummaryUds.Text += $" | Conversations: {_transactions?.Count ?? 0}";
        }

        private void PopulateNrcListView(Dictionary<byte, int> nrcCounts)
        {
            // ListView update pattern: BeginUpdate -> modify -> EndUpdate (reduces flicker)
            lvNrc!.BeginUpdate();
            lvNrc.Items.Clear();

            // Sort: highest count first
            foreach (var kvp in nrcCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
            {
                byte nrc = kvp.Key;
                int count = kvp.Value;

                // Lookup human meaning for NRC
                string meaning =
                    UdsTables.NrcMeaning.TryGetValue(nrc, out var m)
                        ? m
                        : "UnknownNRC";

                // Build row
                var item = new ListViewItem($"0x{nrc:X2}");
                item.SubItems.Add(meaning);
                item.SubItems.Add(count.ToString());

                // Store the raw NRC for click navigation
                item.Tag = nrc;

                lvNrc.Items.Add(item);
            }

            lvNrc.EndUpdate();
        }

        private void PopulateDidListView(Dictionary<ushort, int> didCounts)
        {
            lvDid!.BeginUpdate();
            lvDid.Items.Clear();

            foreach (var kvp in didCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
            {
                ushort did = kvp.Key;
                int count = kvp.Value;

                // Resolve DID -> name
                string name = UdsTables.DescribeDid(did);

                var item = new ListViewItem($"0x{did:X4}");
                item.SubItems.Add(name);
                item.SubItems.Add(count.ToString());

                // Store raw DID for click navigation
                item.Tag = did;

                lvDid.Items.Add(item);
            }

            lvDid.EndUpdate();
        }

        private void LvNrc_ItemActivate(object? sender, EventArgs e)
        {
            // Guard
            if (lvNrc!.SelectedItems.Count <= 0) return;

            // Read NRC from Tag
            byte nrc = (byte)(lvNrc.SelectedItems[0].Tag ?? (byte)0);

            // Put token into search box, then return to decoded tab
            txtSearch!.Text = $"0x{nrc:X2}";
            tabControl!.SelectedTab = tabDecoded!;
        }

        private void LvDid_ItemActivate(object? sender, EventArgs e)
        {
            if (lvDid!.SelectedItems.Count <= 0) return;

            ushort did = (ushort)(lvDid.SelectedItems[0].Tag ?? (ushort)0);

            txtSearch!.Text = $"0x{did:X4}";
            tabControl!.SelectedTab = tabDecoded!;
        }

        // ================================================================
        // SPLITCONTAINER SAFETY (prevents runtime crashes on resize/startup)
        // ================================================================

        private static void SafeSetSplitterDistance(SplitContainer? s, int desired)
        {
            // Safety: null / disposed / handle not created
            if (s == null || s.IsDisposed) return;
            if (!s.IsHandleCreated) return;

            // Determine total size depending on orientation
            int size = (s.Orientation == Orientation.Vertical) ? s.ClientSize.Width : s.ClientSize.Height;
            if (size <= 0) return;

            // Calculate safe min/max values based on panel min sizes and splitter width
            int min = s.Panel1MinSize;
            int max = size - s.SplitterWidth - s.Panel2MinSize;
            if (max < min) return;

            // Clamp desired value into safe bounds
            int clamped = Math.Max(min, Math.Min(desired, max));

            // Apply with defensive try/catch
            try { s.SplitterDistance = clamped; }
            catch (InvalidOperationException)
            {
                // Sometimes layout is still stabilizing; ignore instead of crashing.
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // BeginInvoke runs after the form is displayed and handles exist.
            // This is the safest moment to set PanelMinSize and SplitterDistance.
            BeginInvoke(new Action(() =>
            {
                // ---- Main split (sessions vs tabs) ----
                if (splitMain != null)
                {
                    splitMain.Panel1MinSize = 170;
                    splitMain.Panel2MinSize = 500;
                    SafeSetSplitterDistance(splitMain, 190);
                }

                // ---- Decoded tab splits ----
                if (decodedRootSplit != null)
                {
                    decodedRootSplit.Panel1MinSize = 90;
                    decodedRootSplit.Panel2MinSize = 300;
                    SafeSetSplitterDistance(decodedRootSplit, 90);
                }

                if (decodedBottomSplit != null)
                {
                    decodedBottomSplit.Panel1MinSize = 250;
                    decodedBottomSplit.Panel2MinSize = 200;
                    SafeSetSplitterDistance(decodedBottomSplit, 360);
                }

                if (rawDecodedSplit != null)
                {
                    rawDecodedSplit.Panel1MinSize = 200;
                    rawDecodedSplit.Panel2MinSize = 200;
                    SafeSetSplitterDistance(rawDecodedSplit, Math.Max(250, rawDecodedSplit.ClientSize.Width / 2));
                }

                // ---- Reference tab splits ----
                if (referenceRootSplit != null)
                {
                    referenceRootSplit.Panel1MinSize = 200;
                    referenceRootSplit.Panel2MinSize = 150;
                    SafeSetSplitterDistance(referenceRootSplit, 260);
                }

                if (referenceTopSplit != null)
                {
                    referenceTopSplit.Panel1MinSize = 200;
                    referenceTopSplit.Panel2MinSize = 200;
                    SafeSetSplitterDistance(referenceTopSplit, Math.Max(350, referenceTopSplit.ClientSize.Width / 2));
                }
            }));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // Re-clamp current splitter distances safely on resize
            if (splitMain != null) SafeSetSplitterDistance(splitMain, splitMain.SplitterDistance);
            if (decodedRootSplit != null) SafeSetSplitterDistance(decodedRootSplit, decodedRootSplit.SplitterDistance);
            if (decodedBottomSplit != null) SafeSetSplitterDistance(decodedBottomSplit, decodedBottomSplit.SplitterDistance);
            if (rawDecodedSplit != null) SafeSetSplitterDistance(rawDecodedSplit, rawDecodedSplit.SplitterDistance);
            if (referenceRootSplit != null) SafeSetSplitterDistance(referenceRootSplit, referenceRootSplit.SplitterDistance);
            if (referenceTopSplit != null) SafeSetSplitterDistance(referenceTopSplit, referenceTopSplit.SplitterDistance);
        }

        // ================================================================
        // SMALL UI HELPER (refresh ListBox display after session rename)
        // ================================================================

        private void RefreshSessionListUi()
        {
            // Defensive: list not ready
            if (lstSessions == null) return;
            if (lstSessions.DataSource == null) return;

            // CurrencyManager refresh forces ListBox redraw of DisplayMember text
            if (BindingContext != null)
            {
                if (BindingContext[lstSessions.DataSource] is CurrencyManager cm)
                    cm.Refresh();
            }

            // Force repaint
            lstSessions.Invalidate();
            lstSessions.Update();
        }
    }
}