// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Microsoft.PowerToys.Settings.UI.Helpers
{
    internal static class ResourceLoaderInstance
    {
        internal static ResourceLoader ResourceLoader { get; private set; }

        static ResourceLoaderInstance()
        {
            try
            {
                ResourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader("PowerToys.Settings.pri");
            }
            catch (Exception ex)
            {
                // Initialization can fail (e.g. 0x80073B17 NamedResource Not Found) when
                // running without a package identity or when the .pri file is not yet
                // available. Leave ResourceLoader null so call-sites can handle it
                // gracefully instead of every subsequent access throwing
                // TypeInitializationException and crashing the process.
                System.Diagnostics.Debug.WriteLine($"[ResourceLoaderInstance] Failed to create ResourceLoader: {ex.Message}");
            }
        }
    }
}
