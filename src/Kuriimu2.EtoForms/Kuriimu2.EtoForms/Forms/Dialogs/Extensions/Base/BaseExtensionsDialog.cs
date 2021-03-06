﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Eto.Forms;
using Kore.Batch;
using Kuriimu2.EtoForms.Forms.Models;
using Kuriimu2.EtoForms.Logging;
using Kuriimu2.EtoForms.Resources;
using Kuriimu2.EtoForms.Support;
using Serilog;

namespace Kuriimu2.EtoForms.Forms.Dialogs.Extensions.Base
{
    // HINT: This class can't derive and act as the dialog directly, since in Eto.Forms, once closed a dialog seemingly can't be opened after being closed.
    abstract partial class BaseExtensionsDialog<TExtension, TResult> : Dialog
    {
        private const string TitleName = "{0} Extensions";

        private readonly BatchExtensionProcessor<TExtension, TResult> _batchProcessor;
        private readonly ParameterBuilder _parameterBuilder;

        private bool _isDirectory;

        protected ILogger Logger { get; }

        protected abstract string TypeExtensionName { get; }

        public BaseExtensionsDialog()
        {
            InitializeComponent();

            Logger = new LoggerConfiguration().WriteTo.Sink(new RichTextAreaSink(log)).CreateLogger();
            _batchProcessor = new BatchExtensionProcessor<TExtension, TResult>(ProcessFile, Logger);
            _parameterBuilder = new ParameterBuilder(parameterBox);

            Title = string.Format(TitleName, TypeExtensionName);
            typeLabel.Text = Title + ":";

            extensions.SelectedValueChanged += Extensions_SelectedValueChanged;
            extensions.DataStore = LoadExtensionTypes();
            extensions.SelectedIndex = 0;

            Content.DragEnter += baseExtensionsDialog_DragEnter;
            Content.DragDrop += baseExtensionsDialog_DragDrop;

            #region Commands

            selectFileCommand.Executed += selectFileCommand_Executed;
            selectFolderCommand.Executed += selectFolderCommand_Executed;

            executeCommand.Executed += executeCommand_Executed;

            #endregion
        }

        protected abstract IList<ExtensionType> LoadExtensionTypes();

        protected abstract TExtension CreateExtensionType(ExtensionType selectedExtension);

        protected abstract TResult ProcessFile(TExtension extensionType, string filePath);

        protected abstract void FinalizeProcess(IList<(string, TResult)> results, string rootDir);

        #region Events

        private void selectFolderCommand_Executed(object sender, EventArgs e)
        {
            var sfd = new SelectFolderDialog
            {
                Directory = Settings.Default.TypeExtensionLastDirectory
            };

            if (sfd.ShowDialog(this) != DialogResult.Ok)
                return;

            Settings.Default.TypeExtensionLastDirectory = sfd.Directory;
            Settings.Default.Save();

            selectedPath.Text = sfd.Directory;
            _isDirectory = true;
        }

        private void selectFileCommand_Executed(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Directory = Settings.Default.TypeExtensionLastDirectory == string.Empty ? new Uri(Path.GetFullPath(".")) : new Uri(Settings.Default.TypeExtensionLastDirectory)
            };

            if (ofd.ShowDialog(this) != DialogResult.Ok)
                return;

            Settings.Default.TypeExtensionLastDirectory = Path.GetDirectoryName(ofd.FileName);
            Settings.Default.Save();

            selectedPath.Text = ofd.FileName;
            _isDirectory = false;
        }

        private void executeCommand_Executed(object sender, EventArgs e)
        {
            Execute();
        }

        private void Extensions_SelectedValueChanged(object sender, EventArgs e)
        {
            var extensionType = (ExtensionType)((ComboBox)sender).SelectedValue;
            _parameterBuilder.SetParameters(extensionType.Parameters.Values.ToArray());
        }

        private void baseExtensionsDialog_DragDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data.Uris.Select(x => HttpUtility.UrlDecode(x.AbsolutePath)).ToArray();

            var path = paths[0];
            selectedPath.Text = path;

            _isDirectory = Directory.Exists(path);

            if (autoExecuteBox.Checked ?? false)
                Execute();
        }

        private void baseExtensionsDialog_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragEffects.Copy;
        }

        #endregion

        #region Execution

        private void Execute()
        {
            log.Text = string.Empty;

            if (!VerifyInput())
                return;

            var selectedType = (ExtensionType)extensions.SelectedValue;

            // Create type
            var createdType = CreateExtensionType(selectedType);

            // Execute processing
            // ReSharper disable once PossibleInvalidOperationException
            ExecuteInternal(selectedPath.Text, _isDirectory, subDirectoryBox.Checked.Value, createdType);
        }

        private bool VerifyInput()
        {
            if (extensions.SelectedIndex < 0)
            {
                Logger.Error("Select a {0}.", TypeExtensionName);
                return false;
            }

            if (string.IsNullOrEmpty(selectedPath.Text))
            {
                Logger.Error("Select a file or directory to process.");
                return false;
            }

            if (!subDirectoryBox.Checked.HasValue)
            {
                Logger.Error("Checkbox for sub directories has no state.");
                return false;
            }

            return true;
        }

        private async void ExecuteInternal(string path, bool isDirectory, bool searchSubDirectories, TExtension extensionType)
        {
            ToggleUi(false);

            // Process all files
            var results = await _batchProcessor.Process(path, isDirectory, searchSubDirectories, extensionType);

            // Finalize the processing/create a report
            FinalizeProcess(results, isDirectory ? path : Path.GetDirectoryName(path));

            ToggleUi(true);
        }

        private void ToggleUi(bool toggle)
        {
            extensions.Enabled = toggle;
            parameterBox.Enabled = toggle;

            executeButton.Enabled = selectFileButton.Enabled = selectFolderButton.Enabled = toggle;

            subDirectoryBox.Enabled = toggle;
            autoExecuteBox.Enabled = toggle;
        }

        #endregion
    }
}
