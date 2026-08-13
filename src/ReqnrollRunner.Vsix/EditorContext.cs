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

        /// <summary>
        /// Saves <paramref name="filePath"/> if it is open with unsaved changes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a correctness step, not a courtesy. Everything downstream reads the feature file
        /// from DISK — <c>FeatureFileParser</c> opens the path, and the caret line is an index into
        /// what it finds there. The line number, though, comes from the editor BUFFER. Insert three
        /// lines above a scenario without saving and those two disagree: line 40 in the buffer is
        /// line 37 on disk, so the runner resolves a different scenario and runs it without
        /// complaint. Silently running the wrong test is the exact failure the <c>#line</c> mapping
        /// exists to prevent, and it would be indistinguishable from a mapping bug.
        /// </para>
        /// <para>
        /// Saving is also simply required: Reqnroll regenerates the code-behind from the file on
        /// disk during the build, so an unsaved edit could not have a generated test to match
        /// anyway. Visual Studio's own "run test at cursor" saves for the same reason.
        /// </para>
        /// <para>
        /// Best effort — a read-only file or a failing save must not stop the run, because the
        /// on-disk content is still perfectly runnable.
        /// </para>
        /// </remarks>
        /// <returns><see langword="true"/> when a save actually happened.</returns>
        public static bool SaveIfDirty(DTE2 dte, string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                foreach (Document document in dte.Documents)
                {
                    if (!string.Equals(document.FullName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!document.Saved)
                    {
                        document.Save();
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                // Not open, read-only, or the save was cancelled. The file on disk is still what gets
                // run, and the caller reports the results of that run either way.
            }

            return false;
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
