// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Transactions;
using SLS4All.Compact.Pages;

namespace SLS4All.Compact.Helpers
{
    public static class FrontendHelpers
    {
        public const string IsLocalSessionKey = "local";
        public const string UIScaleXKey = "sx";
        public const string UIScaleYKey = "sy";
        
        public static void Reload(this NavigationManager manager, IMainLayout mainLayout)
        {
            var uri = AddMainQueryStrings(manager.Uri, mainLayout);
            manager.NavigateTo(uri, true, true);
        }

        public static string AddMainQueryStrings(string uri, IMainLayout mainLayout)
        {
            var uriUri = new Uri(uri);
            var args = QueryHelpers.ParseQuery(uriUri.Query);
            if (mainLayout.IsLocalSession)
                args[IsLocalSessionKey] = "1";
            else
                args.Remove(IsLocalSessionKey);
            if (mainLayout.Scale.X != 1)
                args[UIScaleXKey] = mainLayout.Scale.X.ToString(CultureInfo.InvariantCulture);
            else
                args.Remove(UIScaleXKey);
            if (mainLayout.Scale.Y != 1)
                args[UIScaleYKey] = mainLayout.Scale.Y.ToString(CultureInfo.InvariantCulture);
            else
                args.Remove(UIScaleYKey);
            var res = QueryHelpers.AddQueryString(
                uriUri.GetLeftPart(UriPartial.Path), 
                args);
            return res;
        }

        public static bool IsEnabled(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
