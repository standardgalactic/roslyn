﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Options
{
    /// <summary>
    /// Serializes options marked with <see cref="LocalUserProfileStorageLocation"/> to the local hive-specific registry.
    /// </summary>
    internal sealed class LocalUserRegistryOptionPersister : IOptionPersister
    {
        /// <summary>
        /// An object to gate access to <see cref="_registryKey"/>.
        /// </summary>
        private readonly object _gate = new();
        private readonly RegistryKey _registryKey;

        public LocalUserRegistryOptionPersister(IThreadingContext threadingContext, IServiceProvider serviceProvider)
        {
            // Starting with Dev16, the ILocalRegistry service is expected to be free-threaded, and aquiring it from the
            // global service provider is expected to complete without any UI thread marshaling requirements. However,
            // since none of this is publicly documented, we keep this assertion for maximum compatibility assurance.
            // https://docs.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.vsregistry.registryroot
            // https://docs.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ilocalregistry
            // https://docs.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.slocalregistry
            threadingContext.ThrowIfNotOnUIThread();

            _registryKey = VSRegistry.RegistryRoot(serviceProvider, __VsLocalRegistryType.RegType_UserSettings, writable: true);
        }

        private static bool TryGetKeyPathAndName(IOption option, out string path, out string key)
        {
            var serialization = option.StorageLocations.OfType<LocalUserProfileStorageLocation>().SingleOrDefault();

            if (serialization == null)
            {
                path = null;
                key = null;
                return false;
            }
            else
            {
                // We'll just use the filesystem APIs to decompose this
                path = Path.GetDirectoryName(serialization.KeyName);
                key = Path.GetFileName(serialization.KeyName);
                return true;
            }
        }

        bool IOptionPersister.TryFetch(OptionKey optionKey, out object value)
        {
            if (!TryGetKeyPathAndName(optionKey.Option, out var path, out var key))
            {
                value = null;
                return false;
            }

            lock (_gate)
            {
                using var subKey = _registryKey.OpenSubKey(path);
                if (subKey == null)
                {
                    value = null;
                    return false;
                }

                // Options that are of type bool have to be serialized as integers
                if (optionKey.Option.Type == typeof(bool))
                {
                    value = subKey.GetValue(key, defaultValue: (bool)optionKey.Option.DefaultValue ? 1 : 0).Equals(1);
                    return true;
                }
                else if (optionKey.Option.Type == typeof(long))
                {
                    var untypedValue = subKey.GetValue(key, defaultValue: optionKey.Option.DefaultValue);
                    switch (untypedValue)
                    {
                        case string stringValue:
                            {
                                // Due to a previous bug we were accidentally serializing longs as strings.
                                // Gracefully convert those back.
                                var suceeded = long.TryParse(stringValue, out var longValue);
                                value = longValue;
                                return suceeded;
                            }

                        case long longValue:
                            value = longValue;
                            return true;
                    }
                }
                else if (optionKey.Option.Type == typeof(int))
                {
                    var untypedValue = subKey.GetValue(key, defaultValue: optionKey.Option.DefaultValue);
                    switch (untypedValue)
                    {
                        case string stringValue:
                            {
                                // Due to a previous bug we were accidentally serializing ints as strings. 
                                // Gracefully convert those back.
                                var suceeded = int.TryParse(stringValue, out var intValue);
                                value = intValue;
                                return suceeded;
                            }

                        case int intValue:
                            value = intValue;
                            return true;
                    }
                }
                else
                {
                    // Otherwise we can just store normally
                    value = subKey.GetValue(key, defaultValue: optionKey.Option.DefaultValue);
                    return true;
                }
            }

            value = null;
            return false;
        }

        bool IOptionPersister.TryPersist(OptionKey optionKey, object value)
        {
            if (_registryKey == null)
            {
                throw new InvalidOperationException();
            }

            if (!TryGetKeyPathAndName(optionKey.Option, out var path, out var key))
            {
                return false;
            }

            lock (_gate)
            {
                using var subKey = _registryKey.CreateSubKey(path);
                // Options that are of type bool have to be serialized as integers
                if (optionKey.Option.Type == typeof(bool))
                {
                    subKey.SetValue(key, (bool)value ? 1 : 0, RegistryValueKind.DWord);
                    return true;
                }
                else if (optionKey.Option.Type == typeof(long))
                {
                    subKey.SetValue(key, value, RegistryValueKind.QWord);
                    return true;
                }
                else if (optionKey.Option.Type.IsEnum)
                {
                    // If the enum is larger than an int, store as a QWord
                    if (Marshal.SizeOf(Enum.GetUnderlyingType(optionKey.Option.Type)) > Marshal.SizeOf(typeof(int)))
                    {
                        subKey.SetValue(key, (long)value, RegistryValueKind.QWord);
                    }
                    else
                    {
                        subKey.SetValue(key, (int)value, RegistryValueKind.DWord);
                    }

                    return true;
                }
                else
                {
                    subKey.SetValue(key, value);
                    return true;
                }
            }
        }
    }
}
