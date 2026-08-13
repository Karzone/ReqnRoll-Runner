using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace ReqnrollRunner.Vsix.Adornments
{
    /// <summary>
    /// Attaches <see cref="ScenarioActionsAdornment"/> to every editor window that turns out to be
    /// showing a <c>.feature</c> file.
    /// </summary>
    /// <remarks>
    /// Exported for the <c>text</c> content type and filtered by extension at run time, rather than
    /// for a <c>gherkin</c> content type. A <c>.feature</c> file only has a Gherkin content type when
    /// the official Reqnroll extension is installed; without it the file is plain text, and keying on
    /// the content type would silently do nothing for exactly the users who have not installed it.
    /// </remarks>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ScenarioActionsTextViewListener : IWpfTextViewCreationListener
    {
        [Import]
        internal ITextDocumentFactoryService? DocumentFactory { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (DocumentFactory == null ||
                !DocumentFactory.TryGetTextDocument(textView.TextDataModel.DocumentBuffer, out ITextDocument document))
            {
                return;
            }

            string path = document.FilePath ?? string.Empty;
            if (!path.EndsWith(EditorContext.FeatureExtension, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                // The adornment knows nothing about running tests — it reports a line and whether
                // Debug was wanted, and the package does the rest through exactly the same path the
                // context menu uses. One execution path, two ways to reach it.
                _ = new ScenarioActionsAdornment(
                    textView,
                    (line, debug) => ScenarioRunRequest.Raise(path, line, debug));
            }
            catch (Exception)
            {
                // GetAdornmentLayer throws if the layer definition below did not compose, and this
                // runs inside the editor's own view-creation path — an exception here would surface
                // as an error every single time a .feature file is opened. Losing the Run/Debug
                // links is a bad day; making the file unopenable is a worse one, and the context
                // menu still does everything these links do.
            }
        }
    }

    /// <summary>
    /// The seam between an adornment click and the package that can act on it.
    /// </summary>
    /// <remarks>
    /// A MEF-composed adornment has no handle on the <c>AsyncPackage</c>, and the package cannot
    /// import the adornment. A static event is the conventional way across that gap; the package
    /// subscribes once when it loads.
    /// </remarks>
    internal static class ScenarioRunRequest
    {
        public static event Action<string, int, bool>? Requested;

        public static void Raise(string filePath, int line, bool debug)
        {
            Requested?.Invoke(filePath, line, debug);
        }
    }
}
