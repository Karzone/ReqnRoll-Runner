using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ReqnrollRunner.Vsix
{
    /// <summary>Where the caret is: the active document's path and 1-based line.</summary>
    internal sealed class CaretPosition
    {
        public CaretPosition(string filePath, int line)
        {
            FilePath = filePath;
            Line = line;
        }

        public string FilePath { get; }

        /// <summary>1-based, matching what <see cref="Core.Parsing.FeatureFileParser"/> expects.</summary>
        public int Line { get; }
    }

    /// <summary>Reads the active document and caret position out of the shell.</summary>
    internal static class EditorContext
    {
        public const string FeatureExtension = ".feature";

        /// <summary>
        /// The caret position, or <see langword="null"/> when there is no active document.
        /// </summary>
        /// <remarks>
        /// Uses DTE rather than <c>IVsTextManager</c> because the same DTE object is already needed
        /// for building and for attaching the debugger, and <c>TextSelection.CurrentLine</c> is
        /// already 1-based — no off-by-one to get wrong.
        /// </remarks>
        public static CaretPosition? GetCaretPosition(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Document? document = dte.ActiveDocument;
            if (document == null || string.IsNullOrEmpty(document.FullName))
            {
                return null;
            }

            int line = 1;
            if (document.Selection is TextSelection selection)
            {
                line = selection.CurrentLine;
            }

            return new CaretPosition(document.FullName, line < 1 ? 1 : line);
        }

        /// <summary>Whether the active document is a feature file, used to gate command visibility.</summary>
        public static bool IsFeatureFileActive(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Document? document = dte.ActiveDocument;
            if (document == null || string.IsNullOrEmpty(document.FullName))
            {
                return false;
            }

            return document.FullName.EndsWith(FeatureExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
