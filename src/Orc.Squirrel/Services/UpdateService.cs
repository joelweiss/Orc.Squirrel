﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UpdateService.cs" company="WildGums">
//   Copyright (c) 2008 - 2015 WildGums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.Squirrel
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Catel;
    using Catel.Configuration;
    using Catel.Logging;
    using Catel.Reflection;
    using Catel.Services;
    using FileSystem;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Path = Catel.IO.Path;

    /// <summary>
    /// Update service.
    /// </summary>
    public class UpdateService : IUpdateService
    {
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();

        private readonly IConfigurationService _configurationService;
        private readonly IFileService _fileService;
        private readonly IUpdateExecutableLocationService _updateExecutableLocationService;

        private bool _initialized;

        public UpdateService(IConfigurationService configurationService, IFileService fileService,
            IUpdateExecutableLocationService updateExecutableLocationService)
        {
            Argument.IsNotNull(() => configurationService);
            Argument.IsNotNull(() => fileService);
            Argument.IsNotNull(() => updateExecutableLocationService);

            _configurationService = configurationService;
            _fileService = fileService;
            _updateExecutableLocationService = updateExecutableLocationService;

            AvailableChannels = new UpdateChannel[] { };
        }

        /// <summary>
        /// Gets the available availableChannels.
        /// </summary>
        /// <value>The availableChannels.</value>
        public UpdateChannel[] AvailableChannels { get; private set; }

        /// <summary>
        /// Gets or sets the current channel.
        /// </summary>
        /// <value>The current channel.</value>
        public UpdateChannel CurrentChannel
        {
            get
            {
                var channelName = _configurationService.GetRoamingValue(Settings.Application.AutomaticUpdates.UpdateChannel, string.Empty);

                return (from channel in AvailableChannels
                        where channel.Name.EqualsIgnoreCase(channelName)
                        select channel).FirstOrDefault();
            }
            set
            {
                Argument.IsNotNull("value", value);

                _configurationService.SetRoamingValue(Settings.Application.AutomaticUpdates.UpdateChannel, value.Name);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to check for updates.
        /// </summary>
        /// <value><c>true</c> if the check for updates is enabled; otherwise, <c>false</c>.</value>
        public bool CheckForUpdates
        {
            get { return _configurationService.GetRoamingValue(Settings.Application.AutomaticUpdates.CheckForUpdates, false); }
            set { _configurationService.SetRoamingValue(Settings.Application.AutomaticUpdates.CheckForUpdates, value); }
        }

        /// <summary>
        /// Gets a value indicating whether the update system is available.
        /// </summary>
        /// <value><c>true</c> if the is update system is available; otherwise, <c>false</c>.</value>
        public bool IsUpdateSystemAvailable
        {
            get
            {
                var updateExe = _updateExecutableLocationService.FindUpdateExecutable();
                return _fileService.Exists(updateExe);
            }
        }

        /// <summary>
        /// Gets a value indicating whether a new update has been installed.
        /// </summary>
        /// <value><c>true</c> if this instance is updated installed; otherwise, <c>false</c>.</value>
        public bool IsUpdatedInstalled { get; private set; }

        /// <summary>
        /// Occurs when a new update has begun installing.
        /// </summary>
        public event EventHandler<SquirrelEventArgs> UpdateInstalling;

        /// <summary>
        /// Occurs when a progress update happens.
        /// </summary>
        public event EventHandler<SquirrelProgressEventArgs> UpdateProgress;

        /// <summary>
        /// Occurs when a new update has been installed.
        /// </summary>
        public event EventHandler<SquirrelEventArgs> UpdateInstalled;

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        /// <param name="availableChannels">The available channels.</param>
        /// <param name="defaultChannel">The default channel.</param>
        /// <param name="defaultCheckForUpdatesValue">The default value for the check for updates setting.</param>
        public void Initialize(IEnumerable<UpdateChannel> availableChannels, UpdateChannel defaultChannel, bool defaultCheckForUpdatesValue)
        {
            InitializeConfigurationKey(Settings.Application.AutomaticUpdates.CheckForUpdates, defaultCheckForUpdatesValue);
            InitializeConfigurationKey(Settings.Application.AutomaticUpdates.UpdateChannel, defaultChannel.Name);

            var channels = availableChannels.ToArray();

            foreach (var channel in channels)
            {
                InitializeConfigurationKey(Settings.Application.AutomaticUpdates.GetChannelSettingName(channel.Name), channel.DefaultUrl);
            }

            AvailableChannels = channels;

            _initialized = true;
        }

        /// <summary>
        /// Checks for any available updates.
        /// </summary>
        /// <returns><c>true</c> if an update is available; otherwise <c>false</c>.</returns>
        public async Task<SquirrelResult> CheckForUpdatesAsync(SquirrelContext context)
        {
            Argument.IsNotNull(() => context);

            var result = new SquirrelResult
            {
                IsUpdateInstalledOrAvailable = false,
                CurrentVersion = GetCurrentApplicationVersion()
            };

            var channelUrl = GetChannelUrl(context);
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                return result;
            }

            try
            {
                var startInfo = CreateUpdateProcessStartInfo($"--checkForUpdate={channelUrl}");
                var process = Process.Start(startInfo);

                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                var startIndex = output.IndexOf("{");
                if (startIndex > 0)
                {
                    output = output.Substring(startIndex);
                }

                // Results similar to this:
                //{
                //    "currentVersion": "2.3.0-alpha1013",
                //    "futureVersion": "2.3.0-alpha1094",
                //    "releasesToApply": [
                //        {
                //            "version": "2.3.0-alpha1039",
                //            "releaseNotes": ""
                //        },
                //        {
                //            "version": "2.3.0-alpha1074",
                //            "releaseNotes": ""
                //        },
                //        {
                //            "version": "2.3.0-alpha1094",
                //            "releaseNotes": ""
                //        }
                //    ]
                //}

                if (!string.IsNullOrWhiteSpace(output))
                {
                    dynamic releaseInfo = JObject.Parse(output);

                    foreach (var releaseToApply in releaseInfo.releasesToApply)
                    {
                        result.IsUpdateInstalledOrAvailable = true;
                        result.NewVersion = releaseToApply.version;
                    }
                }

                if (!result.IsUpdateInstalledOrAvailable)
                {
                    Log.Info("No updates available");
                }
                else
                {
                    Log.Info($"Found new version '{result.NewVersion}' using url '{channelUrl}'");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while checking for the latest updates");
            }

            return result;
        }

        /// <summary>
        /// Installes the available updates if there is an update available.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task<SquirrelResult> InstallAvailableUpdatesAsync(SquirrelContext context)
        {
            Argument.IsNotNull(() => context);

            var result = new SquirrelResult
            {
                IsUpdateInstalledOrAvailable = false,
                CurrentVersion = GetCurrentApplicationVersion()
            };

            var channelUrl = GetChannelUrl(context);
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                return result;
            }

            try
            {
                // Do we actually have an update? Do a quick one here
                var checkResult = await CheckForUpdatesAsync(context);

                // Note that we don't want the process to stop updating, we only want to invoke
                if (checkResult.IsUpdateInstalledOrAvailable)
                {
                    Log.Info($"Found new version '{checkResult.NewVersion}' using url '{channelUrl}', installing update...");

                    result.NewVersion = checkResult.NewVersion;

                    UpdateInstalling?.Invoke(this, new SquirrelEventArgs(result));
                }
                else
                {
                    Log.Info($"Could not determine whether a new version was available for certain, going to run update anyway...");
                }

                // Executable wrapper
                var startInfo = CreateUpdateProcessStartInfo($"--update={channelUrl}");
                var process = Process.Start(startInfo);

                var line = "0";

                while (!string.IsNullOrWhiteSpace(line))
                {
                    if (int.TryParse(line, out var progress))
                    {
                        RaiseProgressChanged(progress);
                    }

                    line = await process.StandardOutput.ReadLineAsync();
                }

                process.WaitForExit();

                // Only when we knew there was an update pending, we notify
                if (process.ExitCode == 0 && checkResult.IsUpdateInstalledOrAvailable)
                {
                    result.NewVersion = checkResult?.NewVersion ?? "unknown"; 
                    result.IsUpdateInstalledOrAvailable = true;

                    Log.Info("Update installed successfully");

                    IsUpdatedInstalled = true;

                    UpdateInstalled?.Invoke(this, new SquirrelEventArgs(result));

                    Log.Info("Update installed successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while checking for or installing the latest updates");
            }

            return result;
        }

        private ProcessStartInfo CreateUpdateProcessStartInfo(string arguments)
        {
            var updateExecutable = _updateExecutableLocationService.FindUpdateExecutable();
            //var executableFileName = _updateExecutableLocationService.GetApplicationExecutable();

            var startInfo = new ProcessStartInfo(updateExecutable)
            {
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(updateExecutable),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            return startInfo;
        }

        protected virtual void RaiseProgressChanged(int percentage)
        {
            Log.Debug($"Update progress: {percentage}%");

            UpdateProgress?.Invoke(this, new SquirrelProgressEventArgs(percentage));
        }

        /// <summary>
        /// Gets the current application version.
        /// </summary>
        /// <returns></returns>
        protected virtual string GetCurrentApplicationVersion()
        {
            return AssemblyHelper.GetEntryAssembly()?.InformationalVersion();
        }

        /// <summary>
        /// Gets the channel url for the specified context.
        /// </summary>
        /// <returns>The channel url or <c>null</c> if no channel is available.</returns>
        protected string GetChannelUrl(SquirrelContext context)
        {
            if (!_initialized)
            {
                throw Log.ErrorAndCreateException<InvalidOperationException>("Service is not initialized, call Initialize first");
            }

            var checkForUpdates = _configurationService.GetRoamingValue<bool>(Settings.Application.AutomaticUpdates.CheckForUpdates);
            if (!checkForUpdates)
            {
                Log.Info("Automatic updates are disabled");
                return null;
            }

            var channelName = context.ChannelName ?? _configurationService.GetRoamingValue(Settings.Application.AutomaticUpdates.UpdateChannel, string.Empty);
            var channelUrlSettingsName = Settings.Application.AutomaticUpdates.GetChannelSettingName(channelName);
            var channelUrl = _configurationService.GetRoamingValue(channelUrlSettingsName, string.Empty);
            if (string.IsNullOrEmpty(channelUrl))
            {
                Log.Warning("Cannot find url for channel '{0}'", channelName);
                return null;
            }

            var entryAssemblyDirectory = AssemblyHelper.GetEntryAssembly().GetDirectory();
            var updateExe = _updateExecutableLocationService.FindUpdateExecutable();
            if (!_fileService.Exists(updateExe))
            {
                Log.Warning("Cannot check for updates, update.exe is not available");
                return null;
            }

            return channelUrl;
        }

        private void InitializeConfigurationKey(string key, object defaultValue)
        {
            _configurationService.InitializeRoamingValue(key, defaultValue);
        }
    }
}
