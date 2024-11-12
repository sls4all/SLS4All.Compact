// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Scripts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SLS4All.Compact.ComponentModel
{
    public class PrinterTimeManagerSavedOptions
    {
        private bool _resolvedPrinterTimeZone;
        private TimeZoneInfo? _printerTimeZone;

        /// <remarks>
        /// Do not make nullable otherwise saving might cause problems.
        /// </remarks>
        public string PrinterTimeZoneId { get; set; } = "";

        [JsonIgnore]
        public TimeZoneInfo? PrinterTimeZone
        {
            get
            {
                if (!_resolvedPrinterTimeZone)
                {
                    _resolvedPrinterTimeZone = true;
                    _printerTimeZone = !string.IsNullOrEmpty(PrinterTimeZoneId) ? PrinterTimeManager.TryGetTimeZone(PrinterTimeZoneId) : null;
                }
                return _printerTimeZone;
            }
        }

        public PrinterTimeManagerSavedOptions Clone()
            => (PrinterTimeManagerSavedOptions)MemberwiseClone();
    }

    public class PrinterTimeManagerOptions
    {
        public class TimeScriptOptions : LoggedScriptRunnerOptions
        {
        }

        public TimeScriptOptions TimeScript { get; set; } = new();
    }

    public sealed class PrinterTimeManager : IPrinterTimeManager
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        private sealed class ScriptRunner : LoggedScriptRunner<PrinterTimeManager>
        {
            private readonly IOptionsMonitor<PrinterTimeManagerOptions.TimeScriptOptions> _options;
            private readonly IAppDataWriter _appDataWriter;

            public ScriptRunner(
                ILogger logger,
                ILogger<PrinterTimeManager> outputLogger,
                IOptionsMonitor<PrinterTimeManagerOptions.TimeScriptOptions> options,
                IAppDataWriter appDataWriter)
                : base(logger, outputLogger, options)
            {
                _options = options;
                _appDataWriter = appDataWriter;
            }

            public Task<int?> Run(DateTime time, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                var options = _options.CurrentValue;
                var epoch = (long)(time - new DateTime(1970, 1, 1)).TotalSeconds;

                string ReplaceArgs(string text)
                    => text
                        .Replace("{{Epoch}}", epoch.ToString(), StringComparison.OrdinalIgnoreCase);

                var optionsDir = _appDataWriter.GetPublicOptionsDirectory();
                var filename = Path.Combine(optionsDir, options.ExecutablePlatform);
                if (!Path.Exists(filename))
                {
                    filename = options.ExecutablePlatform;
                    if (!Path.Exists(filename))
                        throw new InvalidOperationException($"Missing executable for setting time: {filename} ({Path.GetFullPath(filename)})");
                }

                var args = ReplaceArgs(options.ArgsPlatform);
                return Run(filename, args, cancel);
            }
        }

        private const string IANAtoTZIdMap = "\u0002Australia/Darwin	AUS Central Standard Time\u0003\u0002Australia/Sydney	AUS Eastern Standard Time\u0003\u0002Australia/Melbourne	AUS Eastern Standard Time\u0003\u0002Asia/Kabul	Afghanistan Standard Time\u0003\u0002America/Anchorage	Alaskan Standard Time\u0003\u0002America/Juneau	Alaskan Standard Time\u0003\u0002America/Metlakatla	Alaskan Standard Time\u0003\u0002America/Nome	Alaskan Standard Time\u0003\u0002America/Sitka	Alaskan Standard Time\u0003\u0002America/Yakutat	Alaskan Standard Time\u0003\u0002America/Adak	Aleutian Standard Time\u0003\u0002Asia/Barnaul	Altai Standard Time\u0003\u0002Asia/Riyadh	Arab Standard Time\u0003\u0002Asia/Qatar	Arab Standard Time\u0003\u0002Asia/Dubai	Arabian Standard Time\u0003\u0002Etc/GMT-4	Arabian Standard Time\u0003\u0002Asia/Baghdad	Arabic Standard Time\u0003\u0002America/Argentina/Buenos_Aires	Argentina Standard Time\u0003\u0002America/Argentina/La_Rioja	Argentina Standard Time\u0003\u0002America/Argentina/Rio_Gallegos	Argentina Standard Time\u0003\u0002America/Argentina/Salta	Argentina Standard Time\u0003\u0002America/Argentina/San_Juan	Argentina Standard Time\u0003\u0002America/Argentina/San_Luis	Argentina Standard Time\u0003\u0002America/Argentina/Tucuman	Argentina Standard Time\u0003\u0002America/Argentina/Ushuaia	Argentina Standard Time\u0003\u0002America/Argentina/Catamarca	Argentina Standard Time\u0003\u0002America/Argentina/Cordoba	Argentina Standard Time\u0003\u0002America/Argentina/Jujuy	Argentina Standard Time\u0003\u0002America/Argentina/Mendoza	Argentina Standard Time\u0003\u0002Europe/Astrakhan	Astrakhan Standard Time\u0003\u0002Europe/Ulyanovsk	Astrakhan Standard Time\u0003\u0002America/Halifax	Atlantic Standard Time\u0003\u0002Atlantic/Bermuda	Atlantic Standard Time\u0003\u0002America/Glace_Bay	Atlantic Standard Time\u0003\u0002America/Goose_Bay	Atlantic Standard Time\u0003\u0002America/Moncton	Atlantic Standard Time\u0003\u0002America/Thule	Atlantic Standard Time\u0003\u0002Australia/Eucla	Aus Central W. Standard Time\u0003\u0002Asia/Baku	Azerbaijan Standard Time\u0003\u0002Atlantic/Azores	Azores Standard Time\u0003\u0002America/Scoresbysund	Azores Standard Time\u0003\u0002America/Bahia	Bahia Standard Time\u0003\u0002Asia/Dhaka	Bangladesh Standard Time\u0003\u0002Asia/Thimphu	Bangladesh Standard Time\u0003\u0002Europe/Minsk	Belarus Standard Time\u0003\u0002Pacific/Bougainville	Bougainville Standard Time\u0003\u0002America/Regina	Canada Central Standard Time\u0003\u0002America/Swift_Current	Canada Central Standard Time\u0003\u0002Atlantic/Cape_Verde	Cape Verde Standard Time\u0003\u0002Etc/GMT+1	Cape Verde Standard Time\u0003\u0002Asia/Yerevan	Caucasus Standard Time\u0003\u0002Australia/Adelaide	Cen. Australia Standard Time\u0003\u0002Australia/Broken_Hill	Cen. Australia Standard Time\u0003\u0002America/Guatemala	Central America Standard Time\u0003\u0002America/Belize	Central America Standard Time\u0003\u0002America/Costa_Rica	Central America Standard Time\u0003\u0002Pacific/Galapagos	Central America Standard Time\u0003\u0002America/Tegucigalpa	Central America Standard Time\u0003\u0002America/Managua	Central America Standard Time\u0003\u0002America/El_Salvador	Central America Standard Time\u0003\u0002Etc/GMT+6	Central America Standard Time\u0003\u0002Asia/Almaty	Central Asia Standard Time\u0003\u0002Antarctica/Vostok	Central Asia Standard Time\u0003\u0002Asia/Urumqi	Central Asia Standard Time\u0003\u0002Indian/Chagos	Central Asia Standard Time\u0003\u0002Asia/Bishkek	Central Asia Standard Time\u0003\u0002Asia/Qyzylorda	Central Asia Standard Time\u0003\u0002Etc/GMT-6	Central Asia Standard Time\u0003\u0002America/Cuiaba	Central Brazilian Standard Time\u0003\u0002America/Campo_Grande	Central Brazilian Standard Time\u0003\u0002Europe/Budapest	Central Europe Standard Time\u0003\u0002Europe/Tirane	Central Europe Standard Time\u0003\u0002Europe/Prague	Central Europe Standard Time\u0003\u0002Europe/Belgrade	Central Europe Standard Time\u0003\u0002Europe/Warsaw	Central European Standard Time\u0003\u0002Pacific/Guadalcanal	Central Pacific Standard Time\u0003\u0002Antarctica/Macquarie	Central Pacific Standard Time\u0003\u0002Pacific/Pohnpei	Central Pacific Standard Time\u0003\u0002Pacific/Kosrae	Central Pacific Standard Time\u0003\u0002Pacific/Noumea	Central Pacific Standard Time\u0003\u0002Pacific/Efate	Central Pacific Standard Time\u0003\u0002Etc/GMT-11	Central Pacific Standard Time\u0003\u0002America/Mexico_City	Central Standard Time (Mexico)\u0003\u0002America/Bahia_Banderas	Central Standard Time (Mexico)\u0003\u0002America/Merida	Central Standard Time (Mexico)\u0003\u0002America/Monterrey	Central Standard Time (Mexico)\u0003\u0002America/Chicago	Central Standard Time\u0003\u0002America/Winnipeg	Central Standard Time\u0003\u0002America/Rainy_River	Central Standard Time\u0003\u0002America/Rankin_Inlet	Central Standard Time\u0003\u0002America/Resolute	Central Standard Time\u0003\u0002America/Matamoros	Central Standard Time\u0003\u0002America/Indiana/Knox	Central Standard Time\u0003\u0002America/Indiana/Tell_City	Central Standard Time\u0003\u0002America/Menominee	Central Standard Time\u0003\u0002America/North_Dakota/Beulah	Central Standard Time\u0003\u0002America/North_Dakota/Center	Central Standard Time\u0003\u0002America/North_Dakota/New_Salem	Central Standard Time\u0003\u0002CST6CDT	Central Standard Time\u0003\u0002Pacific/Chatham	Chatham Islands Standard Time\u0003\u0002Asia/Shanghai	China Standard Time\u0003\u0002Asia/Hong_Kong	China Standard Time\u0003\u0002Asia/Macau	China Standard Time\u0003\u0002America/Havana	Cuba Standard Time\u0003\u0002Etc/GMT+12	Dateline Standard Time\u0003\u0002Africa/Nairobi	E. Africa Standard Time\u0003\u0002Antarctica/Syowa	E. Africa Standard Time\u0003\u0002Africa/Juba	E. Africa Standard Time\u0003\u0002Etc/GMT-3	E. Africa Standard Time\u0003\u0002Australia/Brisbane	E. Australia Standard Time\u0003\u0002Australia/Lindeman	E. Australia Standard Time\u0003\u0002Europe/Chisinau	E. Europe Standard Time\u0003\u0002America/Sao_Paulo	E. South America Standard Time\u0003\u0002Pacific/Easter	Easter Island Standard Time\u0003\u0002America/Cancun	Eastern Standard Time (Mexico)\u0003\u0002America/New_York	Eastern Standard Time\u0003\u0002America/Nassau	Eastern Standard Time\u0003\u0002America/Toronto	Eastern Standard Time\u0003\u0002America/Iqaluit	Eastern Standard Time\u0003\u0002America/Nipigon	Eastern Standard Time\u0003\u0002America/Pangnirtung	Eastern Standard Time\u0003\u0002America/Thunder_Bay	Eastern Standard Time\u0003\u0002America/Detroit	Eastern Standard Time\u0003\u0002America/Indiana/Petersburg	Eastern Standard Time\u0003\u0002America/Indiana/Vincennes	Eastern Standard Time\u0003\u0002America/Indiana/Winamac	Eastern Standard Time\u0003\u0002America/Kentucky/Monticello	Eastern Standard Time\u0003\u0002America/Kentucky/Louisville	Eastern Standard Time\u0003\u0002EST5EDT	Eastern Standard Time\u0003\u0002Africa/Cairo	Egypt Standard Time\u0003\u0002Asia/Yekaterinburg	Ekaterinburg Standard Time\u0003\u0002Europe/Kiev	FLE Standard Time\u0003\u0002Europe/Helsinki	FLE Standard Time\u0003\u0002Europe/Sofia	FLE Standard Time\u0003\u0002Europe/Tallinn	FLE Standard Time\u0003\u0002Europe/Vilnius	FLE Standard Time\u0003\u0002Europe/Riga	FLE Standard Time\u0003\u0002Europe/Uzhgorod	FLE Standard Time\u0003\u0002Europe/Zaporozhye	FLE Standard Time\u0003\u0002Pacific/Fiji	Fiji Standard Time\u0003\u0002Europe/London	GMT Standard Time\u0003\u0002Atlantic/Canary	GMT Standard Time\u0003\u0002Atlantic/Faroe	GMT Standard Time\u0003\u0002Europe/Dublin	GMT Standard Time\u0003\u0002Europe/Lisbon	GMT Standard Time\u0003\u0002Atlantic/Madeira	GMT Standard Time\u0003\u0002Europe/Bucharest	GTB Standard Time\u0003\u0002Asia/Nicosia	GTB Standard Time\u0003\u0002Asia/Famagusta	GTB Standard Time\u0003\u0002Europe/Athens	GTB Standard Time\u0003\u0002Asia/Tbilisi	Georgian Standard Time\u0003\u0002America/Godthab	Greenland Standard Time\u0003\u0002Atlantic/Reykjavik	Greenwich Standard Time\u0003\u0002Atlantic/St_Helena	Greenwich Standard Time\u0003\u0002Africa/Abidjan	Greenwich Standard Time\u0003\u0002Africa/Accra	Greenwich Standard Time\u0003\u0002Africa/Bissau	Greenwich Standard Time\u0003\u0002Africa/Monrovia	Greenwich Standard Time\u0003\u0002America/Port-au-Prince	Haiti Standard Time\u0003\u0002Pacific/Honolulu	Hawaiian Standard Time\u0003\u0002Pacific/Rarotonga	Hawaiian Standard Time\u0003\u0002Pacific/Tahiti	Hawaiian Standard Time\u0003\u0002Etc/GMT+10	Hawaiian Standard Time\u0003\u0002Asia/Kolkata	India Standard Time\u0003\u0002Asia/Tehran	Iran Standard Time\u0003\u0002Asia/Jerusalem	Israel Standard Time\u0003\u0002Asia/Amman	Jordan Standard Time\u0003\u0002Europe/Kaliningrad	Kaliningrad Standard Time\u0003\u0002Asia/Kamchatka	Kamchatka Standard Time\u0003\u0002Asia/Seoul	Korea Standard Time\u0003\u0002Africa/Tripoli	Libya Standard Time\u0003\u0002Pacific/Kiritimati	Line Islands Standard Time\u0003\u0002Etc/GMT-14	Line Islands Standard Time\u0003\u0002Australia/Lord_Howe	Lord Howe Standard Time\u0003\u0002Asia/Magadan	Magadan Standard Time\u0003\u0002America/Punta_Arenas	Magallanes Standard Time\u0003\u0002Antarctica/Palmer	Magallanes Standard Time\u0003\u0002Pacific/Marquesas	Marquesas Standard Time\u0003\u0002Indian/Mauritius	Mauritius Standard Time\u0003\u0002Indian/Reunion	Mauritius Standard Time\u0003\u0002Indian/Mahe	Mauritius Standard Time\u0003\u0002Etc/GMT+2	Mid-Atlantic Standard Time\u0003\u0002Asia/Beirut	Middle East Standard Time\u0003\u0002America/Montevideo	Montevideo Standard Time\u0003\u0002Africa/Casablanca	Morocco Standard Time\u0003\u0002Africa/El_Aaiun	Morocco Standard Time\u0003\u0002America/Chihuahua	Mountain Standard Time (Mexico)\u0003\u0002America/Mazatlan	Mountain Standard Time (Mexico)\u0003\u0002America/Denver	Mountain Standard Time\u0003\u0002America/Edmonton	Mountain Standard Time\u0003\u0002America/Cambridge_Bay	Mountain Standard Time\u0003\u0002America/Inuvik	Mountain Standard Time\u0003\u0002America/Yellowknife	Mountain Standard Time\u0003\u0002America/Ojinaga	Mountain Standard Time\u0003\u0002America/Boise	Mountain Standard Time\u0003\u0002MST7MDT	Mountain Standard Time\u0003\u0002Asia/Yangon	Myanmar Standard Time\u0003\u0002Indian/Cocos	Myanmar Standard Time\u0003\u0002Asia/Novosibirsk	N. Central Asia Standard Time\u0003\u0002Africa/Windhoek	Namibia Standard Time\u0003\u0002Asia/Kathmandu	Nepal Standard Time\u0003\u0002Pacific/Auckland	New Zealand Standard Time\u0003\u0002America/St_Johns	Newfoundland Standard Time\u0003\u0002Pacific/Norfolk	Norfolk Standard Time\u0003\u0002Asia/Irkutsk	North Asia East Standard Time\u0003\u0002Asia/Krasnoyarsk	North Asia Standard Time\u0003\u0002Asia/Novokuznetsk	North Asia Standard Time\u0003\u0002Asia/Pyongyang	North Korea Standard Time\u0003\u0002Asia/Omsk	Omsk Standard Time\u0003\u0002America/Santiago	Pacific SA Standard Time\u0003\u0002America/Tijuana	Pacific Standard Time (Mexico)\u0003\u0002America/Los_Angeles	Pacific Standard Time\u0003\u0002America/Vancouver	Pacific Standard Time\u0003\u0002America/Dawson	Pacific Standard Time\u0003\u0002America/Whitehorse	Pacific Standard Time\u0003\u0002PST8PDT	Pacific Standard Time\u0003\u0002Asia/Karachi	Pakistan Standard Time\u0003\u0002America/Asuncion	Paraguay Standard Time\u0003\u0002Europe/Paris	Romance Standard Time\u0003\u0002Europe/Brussels	Romance Standard Time\u0003\u0002Europe/Copenhagen	Romance Standard Time\u0003\u0002Africa/Ceuta	Romance Standard Time\u0003\u0002Europe/Madrid	Romance Standard Time\u0003\u0002Asia/Srednekolymsk	Russia Time Zone 10\u0003\u0002Asia/Anadyr	Russia Time Zone 11\u0003\u0002Europe/Samara	Russia Time Zone 3\u0003\u0002Europe/Moscow	Russian Standard Time\u0003\u0002Europe/Kirov	Russian Standard Time\u0003\u0002Europe/Volgograd	Russian Standard Time\u0003\u0002Europe/Simferopol	Russian Standard Time\u0003\u0002America/Cayenne	SA Eastern Standard Time\u0003\u0002Antarctica/Rothera	SA Eastern Standard Time\u0003\u0002America/Fortaleza	SA Eastern Standard Time\u0003\u0002America/Belem	SA Eastern Standard Time\u0003\u0002America/Maceio	SA Eastern Standard Time\u0003\u0002America/Recife	SA Eastern Standard Time\u0003\u0002America/Santarem	SA Eastern Standard Time\u0003\u0002Atlantic/Stanley	SA Eastern Standard Time\u0003\u0002America/Paramaribo	SA Eastern Standard Time\u0003\u0002Etc/GMT+3	SA Eastern Standard Time\u0003\u0002America/Bogota	SA Pacific Standard Time\u0003\u0002America/Rio_Branco	SA Pacific Standard Time\u0003\u0002America/Eirunepe	SA Pacific Standard Time\u0003\u0002America/Atikokan	SA Pacific Standard Time\u0003\u0002America/Guayaquil	SA Pacific Standard Time\u0003\u0002America/Jamaica	SA Pacific Standard Time\u0003\u0002America/Panama	SA Pacific Standard Time\u0003\u0002America/Lima	SA Pacific Standard Time\u0003\u0002Etc/GMT+5	SA Pacific Standard Time\u0003\u0002America/La_Paz	SA Western Standard Time\u0003\u0002America/Port_of_Spain	SA Western Standard Time\u0003\u0002America/Curacao	SA Western Standard Time\u0003\u0002America/Barbados	SA Western Standard Time\u0003\u0002America/Manaus	SA Western Standard Time\u0003\u0002America/Boa_Vista	SA Western Standard Time\u0003\u0002America/Porto_Velho	SA Western Standard Time\u0003\u0002America/Blanc-Sablon	SA Western Standard Time\u0003\u0002America/Santo_Domingo	SA Western Standard Time\u0003\u0002America/Guyana	SA Western Standard Time\u0003\u0002America/Martinique	SA Western Standard Time\u0003\u0002America/Puerto_Rico	SA Western Standard Time\u0003\u0002Etc/GMT+4	SA Western Standard Time\u0003\u0002Asia/Bangkok	SE Asia Standard Time\u0003\u0002Antarctica/Davis	SE Asia Standard Time\u0003\u0002Indian/Christmas	SE Asia Standard Time\u0003\u0002Asia/Jakarta	SE Asia Standard Time\u0003\u0002Asia/Pontianak	SE Asia Standard Time\u0003\u0002Asia/Ho_Chi_Minh	SE Asia Standard Time\u0003\u0002Etc/GMT-7	SE Asia Standard Time\u0003\u0002America/Miquelon	Saint Pierre Standard Time\u0003\u0002Asia/Sakhalin	Sakhalin Standard Time\u0003\u0002Pacific/Apia	Samoa Standard Time\u0003\u0002Africa/Sao_Tome	Sao Tome Standard Time\u0003\u0002Europe/Saratov	Saratov Standard Time\u0003\u0002Asia/Singapore	Singapore Standard Time\u0003\u0002Asia/Brunei	Singapore Standard Time\u0003\u0002Asia/Makassar	Singapore Standard Time\u0003\u0002Asia/Kuala_Lumpur	Singapore Standard Time\u0003\u0002Asia/Kuching	Singapore Standard Time\u0003\u0002Asia/Manila	Singapore Standard Time\u0003\u0002Etc/GMT-8	Singapore Standard Time\u0003\u0002Africa/Johannesburg	South Africa Standard Time\u0003\u0002Africa/Maputo	South Africa Standard Time\u0003\u0002Etc/GMT-2	South Africa Standard Time\u0003\u0002Asia/Colombo	Sri Lanka Standard Time\u0003\u0002Africa/Khartoum	Sudan Standard Time\u0003\u0002Asia/Damascus	Syria Standard Time\u0003\u0002Asia/Taipei	Taipei Standard Time\u0003\u0002Australia/Hobart	Tasmania Standard Time\u0003\u0002Australia/Currie	Tasmania Standard Time\u0003\u0002America/Araguaina	Tocantins Standard Time\u0003\u0002Asia/Tokyo	Tokyo Standard Time\u0003\u0002Asia/Jayapura	Tokyo Standard Time\u0003\u0002Pacific/Palau	Tokyo Standard Time\u0003\u0002Asia/Dili	Tokyo Standard Time\u0003\u0002Etc/GMT-9	Tokyo Standard Time\u0003\u0002Asia/Tomsk	Tomsk Standard Time\u0003\u0002Pacific/Tongatapu	Tonga Standard Time\u0003\u0002Asia/Chita	Transbaikal Standard Time\u0003\u0002Europe/Istanbul	Turkey Standard Time\u0003\u0002America/Grand_Turk	Turks And Caicos Standard Time\u0003\u0002America/Indiana/Indianapolis	US Eastern Standard Time\u0003\u0002America/Indiana/Marengo	US Eastern Standard Time\u0003\u0002America/Indiana/Vevay	US Eastern Standard Time\u0003\u0002America/Phoenix	US Mountain Standard Time\u0003\u0002America/Dawson_Creek	US Mountain Standard Time\u0003\u0002America/Creston	US Mountain Standard Time\u0003\u0002America/Fort_Nelson	US Mountain Standard Time\u0003\u0002America/Hermosillo	US Mountain Standard Time\u0003\u0002Etc/GMT+7	US Mountain Standard Time\u0003\u0002Etc/GMT-12	UTC+12\u0003\u0002Pacific/Tarawa	UTC+12\u0003\u0002Pacific/Majuro	UTC+12\u0003\u0002Pacific/Kwajalein	UTC+12\u0003\u0002Pacific/Nauru	UTC+12\u0003\u0002Pacific/Funafuti	UTC+12\u0003\u0002Pacific/Wake	UTC+12\u0003\u0002Pacific/Wallis	UTC+12\u0003\u0002Etc/GMT-13	UTC+13\u0003\u0002Pacific/Enderbury	UTC+13\u0003\u0002Pacific/Fakaofo	UTC+13\u0003\u0002Etc/UTC	UTC\u0003\u0002America/Danmarkshavn	UTC\u0003\u0002America/Noronha	UTC-02\u0003\u0002Atlantic/South_Georgia	UTC-02\u0003\u0002Etc/GMT+8	UTC-08\u0003\u0002Pacific/Pitcairn	UTC-08\u0003\u0002Etc/GMT+9	UTC-09\u0003\u0002Pacific/Gambier	UTC-09\u0003\u0002Etc/GMT+11	UTC-11\u0003\u0002Pacific/Pago_Pago	UTC-11\u0003\u0002Pacific/Niue	UTC-11\u0003\u0002Asia/Ulaanbaatar	Ulaanbaatar Standard Time\u0003\u0002Asia/Choibalsan	Ulaanbaatar Standard Time\u0003\u0002America/Caracas	Venezuela Standard Time\u0003\u0002Asia/Vladivostok	Vladivostok Standard Time\u0003\u0002Asia/Ust-Nera	Vladivostok Standard Time\u0003\u0002Australia/Perth	W. Australia Standard Time\u0003\u0002Antarctica/Casey	W. Australia Standard Time\u0003\u0002Africa/Lagos	W. Central Africa Standard Time\u0003\u0002Africa/Algiers	W. Central Africa Standard Time\u0003\u0002Africa/Ndjamena	W. Central Africa Standard Time\u0003\u0002Africa/Tunis	W. Central Africa Standard Time\u0003\u0002Etc/GMT-1	W. Central Africa Standard Time\u0003\u0002Europe/Berlin	W. Europe Standard Time\u0003\u0002Europe/Andorra	W. Europe Standard Time\u0003\u0002Europe/Vienna	W. Europe Standard Time\u0003\u0002Europe/Zurich	W. Europe Standard Time\u0003\u0002Europe/Gibraltar	W. Europe Standard Time\u0003\u0002Europe/Rome	W. Europe Standard Time\u0003\u0002Europe/Luxembourg	W. Europe Standard Time\u0003\u0002Europe/Monaco	W. Europe Standard Time\u0003\u0002Europe/Malta	W. Europe Standard Time\u0003\u0002Europe/Amsterdam	W. Europe Standard Time\u0003\u0002Europe/Oslo	W. Europe Standard Time\u0003\u0002Europe/Stockholm	W. Europe Standard Time\u0003\u0002Asia/Hovd	W. Mongolia Standard Time\u0003\u0002Asia/Tashkent	West Asia Standard Time\u0003\u0002Antarctica/Mawson	West Asia Standard Time\u0003\u0002Asia/Oral	West Asia Standard Time\u0003\u0002Asia/Aqtau	West Asia Standard Time\u0003\u0002Asia/Aqtobe	West Asia Standard Time\u0003\u0002Asia/Atyrau	West Asia Standard Time\u0003\u0002Indian/Maldives	West Asia Standard Time\u0003\u0002Indian/Kerguelen	West Asia Standard Time\u0003\u0002Asia/Dushanbe	West Asia Standard Time\u0003\u0002Asia/Ashgabat	West Asia Standard Time\u0003\u0002Asia/Samarkand	West Asia Standard Time\u0003\u0002Etc/GMT-5	West Asia Standard Time\u0003\u0002Asia/Hebron	West Bank Standard Time\u0003\u0002Asia/Gaza	West Bank Standard Time\u0003\u0002Pacific/Port_Moresby	West Pacific Standard Time\u0003\u0002Antarctica/DumontDUrville	West Pacific Standard Time\u0003\u0002Pacific/Chuuk	West Pacific Standard Time\u0003\u0002Pacific/Guam	West Pacific Standard Time\u0003\u0002Etc/GMT-10	West Pacific Standard Time\u0003\u0002Asia/Yakutsk	Yakutsk Standard Time\u0003\u0002Asia/Khandyga	Yakutsk Standard Time\u0003\u0002Africa/Timbuktu	Greenwich Standard Time\u0003\u0002Africa/Bamako	Greenwich Standard Time\u0003\u0002Africa/Banjul	Greenwich Standard Time\u0003\u0002Africa/Conakry	Greenwich Standard Time\u0003\u0002Africa/Dakar	Greenwich Standard Time\u0003\u0002Africa/Freetown	Greenwich Standard Time\u0003\u0002Africa/Lome	Greenwich Standard Time\u0003\u0002Africa/Nouakchott	Greenwich Standard Time\u0003\u0002Africa/Ouagadougou	Greenwich Standard Time\u0003\u0002Egypt	Egypt Standard Time\u0003\u0002Africa/Maseru	South Africa Standard Time\u0003\u0002Africa/Mbabane	South Africa Standard Time\u0003\u0002Africa/Bangui	W. Central Africa Standard Time\u0003\u0002Africa/Brazzaville	W. Central Africa Standard Time\u0003\u0002Africa/Douala	W. Central Africa Standard Time\u0003\u0002Africa/Kinshasa	W. Central Africa Standard Time\u0003\u0002Africa/Libreville	W. Central Africa Standard Time\u0003\u0002Africa/Luanda	W. Central Africa Standard Time\u0003\u0002Africa/Malabo	W. Central Africa Standard Time\u0003\u0002Africa/Niamey	W. Central Africa Standard Time\u0003\u0002Africa/Porto-Novo	W. Central Africa Standard Time\u0003\u0002Africa/Blantyre	South Africa Standard Time\u0003\u0002Africa/Bujumbura	South Africa Standard Time\u0003\u0002Africa/Gaborone	South Africa Standard Time\u0003\u0002Africa/Harare	South Africa Standard Time\u0003\u0002Africa/Kigali	South Africa Standard Time\u0003\u0002Africa/Lubumbashi	South Africa Standard Time\u0003\u0002Africa/Lusaka	South Africa Standard Time\u0003\u0002Africa/Asmara	E. Africa Standard Time\u0003\u0002Africa/Addis_Ababa	E. Africa Standard Time\u0003\u0002Africa/Dar_es_Salaam	E. Africa Standard Time\u0003\u0002Africa/Djibouti	E. Africa Standard Time\u0003\u0002Africa/Kampala	E. Africa Standard Time\u0003\u0002Africa/Mogadishu	E. Africa Standard Time\u0003\u0002Indian/Antananarivo	E. Africa Standard Time\u0003\u0002Indian/Comoro	E. Africa Standard Time\u0003\u0002Indian/Mayotte	E. Africa Standard Time\u0003\u0002Africa/Asmera	E. Africa Standard Time\u0003\u0002Libya	Libya Standard Time\u0003\u0002America/Atka	Aleutian Standard Time\u0003\u0002US/Aleutian	Aleutian Standard Time\u0003\u0002US/Alaska	Alaskan Standard Time\u0003\u0002America/Buenos_Aires	Argentina Standard Time\u0003\u0002America/Catamarca	Argentina Standard Time\u0003\u0002America/Argentina/ComodRivadavia	Argentina Standard Time\u0003\u0002America/Cordoba	Argentina Standard Time\u0003\u0002America/Rosario	Argentina Standard Time\u0003\u0002America/Jujuy	Argentina Standard Time\u0003\u0002America/Mendoza	Argentina Standard Time\u0003\u0002America/Coral_Harbour	SA Pacific Standard Time\u0003\u0002US/Central	Central Standard Time\u0003\u0002America/Aruba	SA Western Standard Time\u0003\u0002America/Lower_Princes	SA Western Standard Time\u0003\u0002America/Kralendijk	SA Western Standard Time\u0003\u0002America/Shiprock	Mountain Standard Time\u0003\u0002Navajo	Mountain Standard Time\u0003\u0002US/Mountain	Mountain Standard Time\u0003\u0002US/Michigan	Eastern Standard Time\u0003\u0002Canada/Mountain	Mountain Standard Time\u0003\u0002Canada/Atlantic	Atlantic Standard Time\u0003\u0002Cuba	Cuba Standard Time\u0003\u0002America/Fort_Wayne	US Eastern Standard Time\u0003\u0002America/Indianapolis	US Eastern Standard Time\u0003\u0002US/East-Indiana	US Eastern Standard Time\u0003\u0002America/Knox_IN	Central Standard Time\u0003\u0002US/Indiana-Starke	Central Standard Time\u0003\u0002Jamaica	SA Pacific Standard Time\u0003\u0002America/Louisville	Eastern Standard Time\u0003\u0002US/Pacific	Pacific Standard Time\u0003\u0002US/Pacific-New	Pacific Standard Time\u0003\u0002Brazil/West	SA Western Standard Time\u0003\u0002Mexico/BajaSur	Mountain Standard Time (Mexico)\u0003\u0002Mexico/General	Central Standard Time (Mexico)\u0003\u0002US/Eastern	Eastern Standard Time\u0003\u0002Brazil/DeNoronha	UTC-02\u0003\u0002America/Cayman	SA Pacific Standard Time\u0003\u0002US/Arizona	US Mountain Standard Time\u0003\u0002America/Virgin	SA Western Standard Time\u0003\u0002America/Anguilla	SA Western Standard Time\u0003\u0002America/Antigua	SA Western Standard Time\u0003\u0002America/Dominica	SA Western Standard Time\u0003\u0002America/Grenada	SA Western Standard Time\u0003\u0002America/Guadeloupe	SA Western Standard Time\u0003\u0002America/Marigot	SA Western Standard Time\u0003\u0002America/Montserrat	SA Western Standard Time\u0003\u0002America/St_Barthelemy	SA Western Standard Time\u0003\u0002America/St_Kitts	SA Western Standard Time\u0003\u0002America/St_Lucia	SA Western Standard Time\u0003\u0002America/St_Thomas	SA Western Standard Time\u0003\u0002America/St_Vincent	SA Western Standard Time\u0003\u0002America/Tortola	SA Western Standard Time\u0003\u0002Canada/East-Saskatchewan	Canada Central Standard Time\u0003\u0002Canada/Saskatchewan	Canada Central Standard Time\u0003\u0002America/Porto_Acre	SA Pacific Standard Time\u0003\u0002Brazil/Acre	SA Pacific Standard Time\u0003\u0002Chile/Continental	Pacific SA Standard Time\u0003\u0002Brazil/East	E. South America Standard Time\u0003\u0002Canada/Newfoundland	Newfoundland Standard Time\u0003\u0002America/Ensenada	Pacific Standard Time (Mexico)\u0003\u0002Mexico/BajaNorte	Pacific Standard Time (Mexico)\u0003\u0002America/Santa_Isabel	Pacific Standard Time (Mexico)\u0003\u0002Canada/Eastern	Eastern Standard Time\u0003\u0002America/Montreal	Eastern Standard Time\u0003\u0002Canada/Pacific	Pacific Standard Time\u0003\u0002Canada/Yukon	Pacific Standard Time\u0003\u0002Canada/Central	Central Standard Time\u0003\u0002Asia/Ashkhabad	West Asia Standard Time\u0003\u0002Asia/Phnom_Penh	SE Asia Standard Time\u0003\u0002Asia/Vientiane	SE Asia Standard Time\u0003\u0002Asia/Dacca	Bangladesh Standard Time\u0003\u0002Asia/Muscat	Arabian Standard Time\u0003\u0002Asia/Saigon	SE Asia Standard Time\u0003\u0002Hongkong	China Standard Time\u0003\u0002Asia/Tel_Aviv	Israel Standard Time\u0003\u0002Israel	Israel Standard Time\u0003\u0002Asia/Katmandu	Nepal Standard Time\u0003\u0002Asia/Calcutta	India Standard Time\u0003\u0002Asia/Macao	China Standard Time\u0003\u0002Asia/Ujung_Pandang	Singapore Standard Time\u0003\u0002Europe/Nicosia	GTB Standard Time\u0003\u0002Asia/Bahrain	Arab Standard Time\u0003\u0002Asia/Aden	Arab Standard Time\u0003\u0002Asia/Kuwait	Arab Standard Time\u0003\u0002ROK	Korea Standard Time\u0003\u0002Asia/Chongqing	China Standard Time\u0003\u0002Asia/Chungking	China Standard Time\u0003\u0002Asia/Harbin	China Standard Time\u0003\u0002PRC	China Standard Time\u0003\u0002Singapore	Singapore Standard Time\u0003\u0002ROC	Taipei Standard Time\u0003\u0002Iran	Iran Standard Time\u0003\u0002Asia/Thimbu	Bangladesh Standard Time\u0003\u0002Japan	Tokyo Standard Time\u0003\u0002Asia/Ulan_Bator	Ulaanbaatar Standard Time\u0003\u0002Asia/Kashgar	Central Asia Standard Time\u0003\u0002Asia/Rangoon	Myanmar Standard Time\u0003\u0002WET	GMT Standard Time\u0003\u0002Atlantic/Faeroe	GMT Standard Time\u0003\u0002Iceland	Greenwich Standard Time\u0003\u0002Australia/South	Cen. Australia Standard Time\u0003\u0002Australia/Queensland	E. Australia Standard Time\u0003\u0002Australia/Yancowinna	Cen. Australia Standard Time\u0003\u0002Australia/North	AUS Central Standard Time\u0003\u0002Australia/Tasmania	Tasmania Standard Time\u0003\u0002Australia/LHI	Lord Howe Standard Time\u0003\u0002Australia/Victoria	AUS Eastern Standard Time\u0003\u0002Australia/West	W. Australia Standard Time\u0003\u0002Australia/ACT	AUS Eastern Standard Time\u0003\u0002Australia/Canberra	AUS Eastern Standard Time\u0003\u0002Australia/NSW	AUS Eastern Standard Time\u0003\u0002HST	Hawaiian Standard Time\u0003\u0002EST	SA Pacific Standard Time\u0003\u0002MST	US Mountain Standard Time\u0003\u0002Etc/GMT+0	UTC\u0003\u0002Etc/GMT-0	UTC\u0003\u0002Etc/GMT0	UTC\u0003\u0002Etc/Greenwich	UTC\u0003\u0002GMT	UTC\u0003\u0002GMT+0	UTC\u0003\u0002GMT-0	UTC\u0003\u0002GMT0	UTC\u0003\u0002Greenwich	UTC\u0003\u0002Etc/UCT	UTC\u0003\u0002Etc/Universal	UTC\u0003\u0002Etc/Zulu	UTC\u0003\u0002UCT	UTC\u0003\u0002UTC	UTC\u0003\u0002Universal	UTC\u0003\u0002Zulu	UTC\u0003\u0002Etc/GMT	UTC\u0003\u0002Europe/Ljubljana	Central Europe Standard Time\u0003\u0002Europe/Podgorica	Central Europe Standard Time\u0003\u0002Europe/Sarajevo	Central Europe Standard Time\u0003\u0002Europe/Skopje	Central Europe Standard Time\u0003\u0002Europe/Zagreb	Central Europe Standard Time\u0003\u0002MET	W. Europe Standard Time\u0003\u0002EET	GTB Standard Time\u0003\u0002Europe/Tiraspol	E. Europe Standard Time\u0003\u0002Eire	GMT Standard Time\u0003\u0002Europe/Mariehamn	FLE Standard Time\u0003\u0002Asia/Istanbul	Turkey Standard Time\u0003\u0002Turkey	Turkey Standard Time\u0003\u0002Portugal	GMT Standard Time\u0003\u0002Europe/Belfast	GMT Standard Time\u0003\u0002GB	GMT Standard Time\u0003\u0002GB-Eire	GMT Standard Time\u0003\u0002Europe/Jersey	GMT Standard Time\u0003\u0002Europe/Guernsey	GMT Standard Time\u0003\u0002Europe/Isle_of_Man	GMT Standard Time\u0003\u0002W-SU	Russian Standard Time\u0003\u0002Atlantic/Jan_Mayen	W. Europe Standard Time\u0003\u0002Arctic/Longyearbyen	W. Europe Standard Time\u0003\u0002CET	Romance Standard Time\u0003\u0002Europe/Bratislava	Central Europe Standard Time\u0003\u0002Europe/Vatican	W. Europe Standard Time\u0003\u0002Europe/San_Marino	W. Europe Standard Time\u0003\u0002Poland	Central European Standard Time\u0003\u0002Europe/Busingen	W. Europe Standard Time\u0003\u0002Europe/Vaduz	W. Europe Standard Time\u0003\u0002Antarctica/South_Pole	New Zealand Standard Time\u0003\u0002NZ	New Zealand Standard Time\u0003\u0002Antarctica/McMurdo	New Zealand Standard Time\u0003\u0002NZ-CHAT	Chatham Islands Standard Time\u0003\u0002Pacific/Truk	West Pacific Standard Time\u0003\u0002Pacific/Yap	West Pacific Standard Time\u0003\u0002Chile/EasterIsland	Easter Island Standard Time\u0003\u0002Pacific/Saipan	West Pacific Standard Time\u0003\u0002US/Hawaii	Hawaiian Standard Time\u0003\u0002Pacific/Johnston	Hawaiian Standard Time\u0003\u0002Kwajalein	UTC+12\u0003\u0002Pacific/Samoa	UTC-11\u0003\u0002US/Samoa	UTC-11\u0003\u0002Pacific/Midway	UTC-11\u0003\u0002Pacific/Ponape	Central Pacific Standard Time\u0003";
        private readonly ILogger<PrinterTimeManager> _logger;
        private readonly IOptionsMonitor<PrinterTimeManagerOptions> _options;
        private readonly IOptionsWriter<PrinterTimeManagerSavedOptions> _savedOptionsWriter;
        private readonly IAppDataWriter _appDataWriter;
        private readonly IJSRuntime _jsRuntime;
        private Task<TimeZoneInfo?>? _timeZoneBrowserTask;
        private (bool hasResolved, TimeZoneInfo? tz) _timeZoneBrowser;

        public PrinterTimeManager(
            ILogger<PrinterTimeManager> logger,
            IOptionsMonitor<PrinterTimeManagerOptions> options,
            IOptionsWriter<PrinterTimeManagerSavedOptions> savedOptionsWriter,
            IAppDataWriter appDataWriter,
            IJSRuntime jsRuntime)
        {
            _logger = logger;
            _options = options;
            _savedOptionsWriter = savedOptionsWriter;
            _appDataWriter = appDataWriter;
            _jsRuntime = jsRuntime;
        }

        public static TimeZoneInfo? TryGetTimeZone(string ianaOrId)
        {
            var displayName = GetTimeZoneIdFromIANA(ianaOrId);
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
            {
                if (tz.Id == ianaOrId || tz.Id == displayName || tz.DisplayName == displayName || tz.StandardName == displayName)
                    return tz;
            }
            return null;
        }

        /// <summary>
        /// Convert IANA time zone name to .NET time zone id.
        /// </summary>
        public static string? GetTimeZoneIdFromIANA(string? ianaName)
        {
            if (string.IsNullOrWhiteSpace(ianaName))
                return null;

            var searchText = "\u0002" + ianaName + "\t";
            var headPos = IANAtoTZIdMap.IndexOf(searchText);
            if (headPos == -1)
                return null;

            var midPos = headPos + searchText.Length - 1;
            var termPos = IANAtoTZIdMap.IndexOf('\u0003', midPos);
            var tzid = IANAtoTZIdMap.Substring(midPos, termPos - midPos);
            return tzid;
        }

        public async Task<bool> TrySetPrinterDateTimeUtc(DateTime time, CancellationToken cancel)
        {
            if (time.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Time is not in UTC time zone");
            var options = _options.CurrentValue;
            cancel.ThrowIfCancellationRequested();
            try
            {
                if (!string.IsNullOrWhiteSpace(options.TimeScript.ExecutablePlatform))
                {
                    var runner = new ScriptRunner(_logger, _logger, TransformOptionsMonitor.Create(_options, x => x.TimeScript), _appDataWriter);
                    var res = await runner.Run(time, cancel);
                    if (res == 0)
                        return true;
                    else
                    {
                        _logger.LogError($"Failed to set system time, result is non-zero: {res}");
                        return false;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    SYSTEMTIME data = new();
                    data.wYear = (ushort)time.Year;
                    data.wMonth = (ushort)time.Month;
                    data.wDayOfWeek = (ushort)time.DayOfWeek;
                    data.wDay = (ushort)time.Day;
                    data.wHour = (ushort)time.Hour;
                    data.wMinute = (ushort)time.Minute;
                    data.wSecond = (ushort)time.Second;
                    data.wMilliseconds = (ushort)time.Millisecond;
                    if (SetWindowsTime(ref data) != 0)
                        return true;
                    else
                    {
                        _logger.LogError($"Failed to set system time, result is zero");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError($"Failed to set system time, unsupported platform and missing script config");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to set system time");
                return false;
            }
        }

        public Task SetPrinterTimeZone(TimeZoneInfo? timeZone, CancellationToken cancel)
        {
            var value = _savedOptionsWriter.CurrentValue.Clone();
            value.PrinterTimeZoneId = timeZone?.Id ?? "";
            return _savedOptionsWriter.Write(value, cancel);
        }

        public ValueTask<TimeZoneInfo?> GetTimeZone(bool isLocalSession, bool fallbackToLocal = false)
        {
            TimeZoneInfo? tz;
            if (isLocalSession)
            {
                var options = _savedOptionsWriter.CurrentValue;
                tz = options.PrinterTimeZone;
            }
            else
            {
                if (_timeZoneBrowser.hasResolved)
                    tz = _timeZoneBrowser.tz;
                else
                {
                    if (_timeZoneBrowserTask == null)
                        _timeZoneBrowserTask = GetTimeZoneFromBrowser(fallbackToLocal);
                    return new ValueTask<TimeZoneInfo?>(_timeZoneBrowserTask);
                }
            }
            if (fallbackToLocal && tz == null)
                tz = TimeZoneInfo.Local;
            return ValueTask.FromResult(tz);
        }

        private async Task<TimeZoneInfo?> GetTimeZoneFromBrowser(bool fallbackToLocal)
        {
            TimeZoneInfo? tz = null;
            try
            {
                if (_jsRuntime.GuessIsInitialized())
                {
                    var ianaName = await _jsRuntime.InvokeAsync<string>("getBrowserTimeZone");
                    tz = TryGetTimeZone(ianaName);
                }
            }
            catch (InvalidOperationException) // prerendering
            {
                // swallow
            }
            _timeZoneBrowser = (true, tz);
            if (fallbackToLocal && tz == null)
                tz = TimeZoneInfo.Local;
            return tz;
        }


        [DllImport("kernel32", EntryPoint = "SetSystemTime")]
        private extern static uint SetWindowsTime(ref SYSTEMTIME lpSystemTime);
    }
}
