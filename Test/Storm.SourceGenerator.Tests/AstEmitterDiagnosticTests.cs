using Storm.SourceGenerator.Emit;

namespace Storm.SourceGenerator.Tests
{
    /// <summary>
    /// Tests verifying that AstEmitter.EmitRuntimeTypes() emits the
    /// DiagnosticEntry struct and updated DiagnosticBag class with
    /// structured diagnostic support.
    /// </summary>
    public class AstEmitterDiagnosticTests
    {
        private readonly string _output = AstEmitter.EmitRuntimeTypes();

        // ---------------------------------------------------------------
        //  DiagnosticEntry struct tests (Requirement 1.1)
        // ---------------------------------------------------------------

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_IsReadonlyStruct()
        {
            Assert.Contains("public readonly struct DiagnosticEntry", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_HasMessageProperty()
        {
            Assert.Contains("public string Message { get; }", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_HasFilePathProperty()
        {
            Assert.Contains("public string? FilePath { get; }", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_HasLineProperty()
        {
            Assert.Contains("public int Line { get; }", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_HasColumnProperty()
        {
            Assert.Contains("public int Column { get; }", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_HasConstructorWithAllFourParams()
        {
            Assert.Contains(
                "public DiagnosticEntry(string message, string? filePath, int line, int column)",
                _output);
        }

        // ---------------------------------------------------------------
        //  DiagnosticBag class tests (Requirement 1.4)
        // ---------------------------------------------------------------

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_StoresListOfDiagnosticEntry()
        {
            Assert.Contains("List<DiagnosticEntry>", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_HasAddOverloadWithFourParams()
        {
            Assert.Contains(
                "public void Add(string message, string? filePath, int line, int column)",
                _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_HasAddOverloadWithOneParam()
        {
            Assert.Contains("public void Add(string message)", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_HasEntriesProperty()
        {
            Assert.Contains("IReadOnlyList<DiagnosticEntry> Entries", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_HasBackwardCompatibleMessagesProperty()
        {
            Assert.Contains("IReadOnlyList<string> Messages", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_MessagesSelectsFromEntries()
        {
            // Messages should derive from entries, not from a separate list
            Assert.Contains("_entries.Select(e => e.Message)", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_HasHasErrorsProperty()
        {
            Assert.Contains("public bool HasErrors => _entries.Count > 0;", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_HasCountProperty()
        {
            Assert.Contains("public int Count => _entries.Count;", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_TruncateToOperatesOnEntries()
        {
            Assert.Contains("public void TruncateTo(int count)", _output);
            Assert.Contains("_entries.RemoveRange(count, _entries.Count - count)", _output);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_SingleArgAddCreatesEntryWithDefaults()
        {
            // The single-arg Add should create an entry with null path, 0 line, 0 column
            // Find the single-arg Add method body
            var singleArgAddIndex = _output.IndexOf("public void Add(string message)");
            Assert.True(singleArgAddIndex >= 0, "Single-arg Add method not found");

            // The body should create a DiagnosticEntry with null, 0, 0
            var bodyAfterAdd = _output.Substring(singleArgAddIndex, 200);
            Assert.Contains("new DiagnosticEntry(message, null, 0, 0)", bodyAfterAdd);
        }

        [Fact]
        public void EmitRuntimeTypes_DiagnosticBag_FourArgAddCreatesDiagnosticEntry()
        {
            var fourArgAddIndex = _output.IndexOf("public void Add(string message, string? filePath, int line, int column)");
            Assert.True(fourArgAddIndex >= 0, "Four-arg Add method not found");

            var bodyAfterAdd = _output.Substring(fourArgAddIndex, 200);
            Assert.Contains("new DiagnosticEntry(message, filePath, line, column)", bodyAfterAdd);
        }

        // ---------------------------------------------------------------
        //  DiagnosticEntry is in Storm.Runtime namespace
        // ---------------------------------------------------------------

        [Fact]
        public void EmitRuntimeTypes_DiagnosticEntry_IsInStormRuntimeNamespace()
        {
            // DiagnosticEntry should appear inside the Storm.Runtime namespace block
            var nsIndex = _output.IndexOf("namespace Storm.Runtime");
            var entryIndex = _output.IndexOf("public readonly struct DiagnosticEntry");
            Assert.True(nsIndex >= 0, "Storm.Runtime namespace not found");
            Assert.True(entryIndex >= 0, "DiagnosticEntry not found");
            Assert.True(entryIndex > nsIndex, "DiagnosticEntry should be inside Storm.Runtime namespace");
        }
    }
}
