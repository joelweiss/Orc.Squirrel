﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="IUpdateService.cs" company="Wild Gums">
//   Copyright (c) 2008 - 2014 Wild Gums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orcomp.Squirrel
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Orc.Squirrel;

    /// <summary>
    /// Update service.
    /// </summary>
    public interface IUpdateService
    {
        /// <summary>
        /// Gets a value indicating whether a new update has been installed.
        /// </summary>
        /// <value><c>true</c> if this instance is updated installed; otherwise, <c>false</c>.</value>
        bool IsUpdatedInstalled { get; }

        /// <summary>
        /// Occurs when a new update has been installed.
        /// </summary>
        event EventHandler<EventArgs> UpdateInstalled;

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        /// <param name="availableChannels">The available channels.</param>
        /// <param name="defaultChannel">The default channel.</param>
        /// <param name="defaultCheckForUpdatesValue">The default value for the check for updates setting.</param>
        void Initialize(IEnumerable<UpdateChannel> availableChannels, UpdateChannel defaultChannel, bool defaultCheckForUpdatesValue);

        /// <summary>
        /// Handles the updates by installing them if there is an update available.
        /// </summary>
        /// <returns>Task.</returns>
        Task HandleUpdates();

        /// <summary>
        /// Gets the available availableChannels.
        /// </summary>
        /// <value>The availableChannels.</value>
        UpdateChannel[] AvailableChannels { get; }

        /// <summary>
        /// Gets or sets the current channel.
        /// </summary>
        /// <value>The current channel.</value>
        UpdateChannel CurrentChannel { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to check for updates.
        /// </summary>
        /// <value><c>true</c> if the check for updates is enabled; otherwise, <c>false</c>.</value>
        bool CheckForUpdates { get; set; }

        /// <summary>
        /// Gets a value indicating whether the update system is available.
        /// </summary>
        /// <value><c>true</c> if the is update system is available; otherwise, <c>false</c>.</value>
        bool IsUpdateSystemAvailable { get; }
    }
}