﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LegendaryExplorerCore.Misc;
using LegendaryExplorerCore.Packages;
using ME3TweaksCore.Helpers;
using ME3TweaksCoreWPF;
using ME3TweaksCoreWPF.Targets;
using ME3TweaksCoreWPF.UI;
using ME3TweaksModManager.modmanager.helpers;
using ME3TweaksModManager.modmanager.loaders;
using ME3TweaksModManager.modmanager.localizations;
using ME3TweaksModManager.modmanager.objects.batch;
using ME3TweaksModManager.modmanager.objects.mod;
using ME3TweaksModManager.modmanager.usercontrols.interfaces;
using ME3TweaksModManager.modmanager.windows;
using ME3TweaksModManager.ui;
using Microsoft.AppCenter.Analytics;
using Newtonsoft.Json;
using MemoryAnalyzer = ME3TweaksModManager.modmanager.memoryanalyzer.MemoryAnalyzer;

namespace ME3TweaksModManager.modmanager.usercontrols
{
    /// <summary>
    /// Interaction logic for BatchModLibrary.xaml
    /// </summary>
    public partial class BatchModLibrary : MMBusyPanelBase
    {
        public BatchLibraryInstallQueue SelectedBatchQueue { get; set; }
        public BatchMod SelectedModInGroup { get; set; }
        public ObservableCollectionExtended<BatchLibraryInstallQueue> AvailableBatchQueues { get; } = new ObservableCollectionExtended<BatchLibraryInstallQueue>();
        public ObservableCollectionExtended<GameTargetWPF> InstallationTargetsForGroup { get; } = new ObservableCollectionExtended<GameTargetWPF>();
        public BatchModLibrary()
        {
            MemoryAnalyzer.AddTrackedMemoryItem(@"Batch Mod Installer Panel", new WeakReference(this));
            LoadCommands();
        }
        public ICommand CloseCommand { get; private set; }
        public ICommand CreateNewGroupCommand { get; private set; }
        public ICommand InstallGroupCommand { get; private set; }
        public ICommand EditGroupCommand { get; private set; }
        public ICommand DeleteGroupCommand { get; private set; }
        public bool CanCompressPackages => SelectedBatchQueue != null && SelectedBatchQueue.Game is MEGame.ME2 or MEGame.ME3;

        private void LoadCommands()
        {
            CloseCommand = new GenericCommand(ClosePanel);
            CreateNewGroupCommand = new GenericCommand(CreateNewGroup);
            InstallGroupCommand = new GenericCommand(InstallGroup, CanInstallGroup);
            EditGroupCommand = new GenericCommand(EditGroup, BatchQueueSelected);
            DeleteGroupCommand = new GenericCommand(DeleteGroup, BatchQueueSelected);
        }

        private void DeleteGroup()
        {
            var result = M3L.ShowDialog(mainwindow, M3L.GetString(M3L.string_interp_deleteTheSelectedBatchQueue, SelectedBatchQueue.QueueName), M3L.GetString(M3L.string_confirmDeletion), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                File.Delete(Path.Combine(M3Filesystem.GetBatchInstallGroupsFolder(), SelectedBatchQueue.BackingFilename));
                AvailableBatchQueues.Remove(SelectedBatchQueue);
                SelectedBatchQueue = null;
            }
        }

        private void EditGroup()
        {
            var editGroupUI = new BatchModQueueEditor(mainwindow, SelectedBatchQueue);
            // Original code.
            editGroupUI.ShowDialog();
            var newPath = editGroupUI.SavedPath;
            if (newPath != null)
            {
                //file was saved, reload
                parseBatchFiles(newPath);
            }


#if DEBUG
            // Debug code. Requires commenting out the above.
            //editGroupUI.Show();
#endif
        }

        private bool BatchQueueSelected() => SelectedBatchQueue != null;

        private void InstallGroup()
        {
            TelemetryInterposer.TrackEvent(@"Installing Batch Group", new Dictionary<string, string>()
            {
                {@"Group name", SelectedBatchQueue.QueueName},
                {@"Group size", SelectedBatchQueue.ModsToInstall.Count.ToString()},
                {@"Game", SelectedBatchQueue.Game.ToString()}
            });
            OnClosing(new DataEventArgs(SelectedBatchQueue));
        }

        private bool CanInstallGroup()
        {
            return SelectedGameTarget != null && SelectedBatchQueue != null;
        }

        private void CreateNewGroup()
        {
            var editGroupUI = new BatchModQueueEditor(mainwindow);
            editGroupUI.ShowDialog();
            var newPath = editGroupUI.SavedPath;
            if (newPath != null)
            {
                //file was saved, reload
                parseBatchFiles(newPath);
            }
        }

        private void ClosePanel()
        {
            OnClosing(DataEventArgs.Empty);
        }

        public override void HandleKeyPress(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                OnClosing(DataEventArgs.Empty);
            }
        }

        public override void OnPanelVisible()
        {
            InitializeComponent();
            parseBatchFiles();
        }

        private void parseBatchFiles(string pathToHighlight = null)
        {
            AvailableBatchQueues.ClearEx();
            var batchDir = M3Filesystem.GetBatchInstallGroupsFolder();
            var files = Directory.GetFiles(batchDir);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension is @".biq2" or @".biq" or @".txt")
                {
                    var queue = BatchLibraryInstallQueue.ParseInstallQueue(file);
                    if (queue != null && queue.Game.IsEnabledGeneration())
                    {
                        AvailableBatchQueues.Add(queue);
                        if (file == pathToHighlight)
                        {
                            SelectedBatchQueue = queue;
                        }
                    }
                }
            }
        }

        public GameTargetWPF SelectedGameTarget { get; set; }

        private void OnSelectedBatchQueueChanged()
        {
            GameTargetWPF currentTarget = SelectedGameTarget;
            SelectedGameTarget = null;
            InstallationTargetsForGroup.ClearEx();
            if (SelectedBatchQueue != null)
            {
                InstallationTargetsForGroup.AddRange(mainwindow.InstallationTargets.Where(x => x.Game == SelectedBatchQueue.Game));
                if (InstallationTargetsForGroup.Contains(currentTarget))
                {
                    SelectedGameTarget = currentTarget;
                }
                else
                {
                    SelectedGameTarget = InstallationTargetsForGroup.FirstOrDefault();
                }

                if (SelectedBatchQueue.ModsToInstall.Any())
                {
                    SelectedModInGroup = SelectedBatchQueue.ModsToInstall.First();
                }

                if (SelectedBatchQueue.Game == MEGame.ME1) SelectedBatchQueue.InstallCompressed = false;
            }
            TriggerPropertyChangedFor(nameof(CanCompressPackages));
        }

        public string ModDescriptionText { get; set; }

        public void OnSelectedModInGroupChanged()
        {
            if (SelectedModInGroup == null)
            {
                ModDescriptionText = "";
            }
            else
            {
                ModDescriptionText = SelectedModInGroup.Mod?.DisplayedModDescription ?? "Mod not available for install";
            }
        }

        // ISizeAdjustbale Interface
        public override double MaxWindowWidthPercent { get; set; } = 0.85;
        public override double MaxWindowHeightPercent { get; set; } = 0.85;
    }

}
