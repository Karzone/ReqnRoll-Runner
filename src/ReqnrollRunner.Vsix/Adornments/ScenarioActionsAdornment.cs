using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;
using ReqnrollRunner.Core.Parsing;

namespace ReqnrollRunner.Vsix.Adornments
{
    /// <summary>
    /// Draws a clickable <c>Run | Debug</c> line above every scenario in a <c>.feature</c> file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SPEC §7 asks for "CodeLens-style adornments", and this is deliberately *style* rather than
    /// literal CodeLens. Real CodeLens for a non-Roslyn content type means an out-of-process
    /// <c>IAsyncCodeLensDataPointProvider</c> hosted in ServiceHub, plus callback plumbing to get
    /// back into the IDE to actually run something. That is a great deal of version-sensitive code
    /// for the same visual result, and none of it can be tested outside a running Visual Studio.
    /// An in-process adornment gets the same affordance with a fraction of the surface.
    /// </para>
    /// <para>
    /// Keyed to the <c>text</c> content type and filtered by file extension at run time, for the same
    /// reason command visibility is decided in code: a <c>.feature</c> file's content type depends on
    /// whether the official Reqnroll extension is installed, so it cannot be relied on.
    /// </para>
    /// </remarks>
    internal sealed class ScenarioActionsAdornment
    {
        private const string LayerName = "ReqnrollRunnerScenarioActions";

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly Action<int, bool> _invoke;

        /// <summary>Scenario keyword lines, 1-based. Recomputed when the buffer changes.</summary>
        private HashSet<int> _scenarioLines = new HashSet<int>();

        public ScenarioActionsAdornment(IWpfTextView view, Action<int, bool> invoke)
        {
            _view = view;
            _invoke = invoke;
            _layer = view.GetAdornmentLayer(LayerName);

            RecomputeScenarioLines();

            _view.LayoutChanged += OnLayoutChanged;
            _view.TextBuffer.PostChanged += OnBufferChanged;
            _view.Closed += OnClosed;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.LayoutChanged -= OnLayoutChanged;
            _view.TextBuffer.PostChanged -= OnBufferChanged;
            _view.Closed -= OnClosed;
        }

        /// <summary>
        /// Reparses on edit. Parsing the whole file on every keystroke would be felt, so this only
        /// runs once the buffer settles — <c>PostChanged</c> fires after a batch of edits, not per
        /// character.
        /// </summary>
        private void OnBufferChanged(object sender, EventArgs e)
        {
            RecomputeScenarioLines();
        }

        private void RecomputeScenarioLines()
        {
            var lines = new HashSet<int>();

            try
            {
                // Parse from the buffer, not from disk: the adornments must track unsaved edits.
                foreach (int line in FeatureOutline.ScenarioLines(_view.TextBuffer.CurrentSnapshot.GetText()))
                {
                    lines.Add(line);
                }
            }
            catch (Exception)
            {
                // A half-typed feature file does not parse, which is completely normal while editing.
                // Showing no adornments is the right answer; throwing would take the editor with it.
            }

            _scenarioLines = lines;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            foreach (ITextViewLine line in e.NewOrReformattedLines)
            {
                DrawIfScenario(line);
            }
        }

        private void DrawIfScenario(ITextViewLine line)
        {
            int lineNumber = _view.TextSnapshot.GetLineNumberFromPosition(line.Start) + 1;
            if (!_scenarioLines.Contains(lineNumber))
            {
                return;
            }

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0),
            };

            panel.Children.Add(CreateLink("Run", () => _invoke(lineNumber, false)));
            panel.Children.Add(new TextBlock
            {
                Text = "  |  ",
                Opacity = 0.5,
                Margin = new Thickness(0),
            });
            panel.Children.Add(CreateLink("Debug", () => _invoke(lineNumber, true)));

            // Sit just above the scenario's own line, indented to match it.
            Canvas.SetLeft(panel, line.Left);
            Canvas.SetTop(panel, line.Top - line.Height);

            _layer.AddAdornment(
                AdornmentPositioningBehavior.TextRelative,
                new SnapshotSpan(line.Start, line.End),
                null,
                panel,
                null);
        }

        private static UIElement CreateLink(string text, Action onClick)
        {
            var link = new Hyperlink(new Run(text))
            {
                // The editor's own font, at a slightly smaller size, so it reads as editor chrome
                // rather than as content.
                TextDecorations = null,
            };

            link.Click += (_, __) => onClick();

            return new TextBlock(link)
            {
                FontSize = 11,
                Opacity = 0.85,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0),
            };
        }
    }

    /// <summary>Registers the layer the adornments are drawn into.</summary>
    internal static class ScenarioActionsAdornmentLayer
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("ReqnrollRunnerScenarioActions")]
        [Order(After = PredefinedAdornmentLayers.Selection, Before = PredefinedAdornmentLayers.Text)]
        internal static AdornmentLayerDefinition? Definition { get; set; }
    }
}
