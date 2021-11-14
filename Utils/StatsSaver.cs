using Newtonsoft.Json;
using StatsStoreHelper.GoogleApi;
using StatsStoreHelper.MyWrappers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace StatsStoreHelper.Utils
{
    public class StatsSaver
    {
        private static StatsSaver instance;
        private static readonly object instanceLock = new object();
        private bool statsSaved;
        private string stashPath;

        public string ScreenshotPath { get; set; }

        private StatsSaver()
        {
            statsSaved = false;
            stashPath = Path.Combine(BepInEx.Paths.ConfigPath, PluginInfo.PLUGIN_NAME, "stash");
        }

        internal static StatsSaver GetInstance()
        {
            if(instance == null)
                lock(instanceLock)
                    if(instance == null)
                        instance = new StatsSaver();
            return instance;
        }

        public async void Save()
        {
            if(this.statsSaved)
                return;

            MySongEntry currentSongEntry = MyGlobalVariables.GetInstance().CurrentSongEntry;
            MyPlayerSongStats playerSongStats = MySongStats.PlayerSongStats[0];

            string hash = currentSongEntry.GetSHA256Hash();

            BackUpScreenshot();
            
            StatsRowBuilder statsRowBuilder = new StatsRowBuilder();
            Dictionary<string, object> stats = new Dictionary<string, object>
            {
                { "%date%", DateTime.Now },
                { "%artist%", currentSongEntry.Artist },
                { "%song%", currentSongEntry.Name },
                { "%source%", currentSongEntry.iconName },
                { "%charter%", currentSongEntry.Charter },
                { "%score%", playerSongStats.Score },
                { "%stars%", MySongStats.Stars },
                { "%accuracy%", Convert.ToDouble(playerSongStats.Accuracy.TrimEnd('%')) / 100 },
                { "%sp%", $"{playerSongStats.spPhrasesHit}/{playerSongStats.spPhrasesAll}" },
                { "%fc%", (playerSongStats.combo == playerSongStats.notesAll) ? true : false },
                { "%screenshot%", ScreenshotPath },
                { "%hash%", hash }
            };

            foreach(string tag in UserConfig.UserStatsTags)
                statsRowBuilder.AddStat(tag, stats[tag]);
            
            try
            {
                string screenshotUrl = await UploadScreenshot();
                stats["%screenshot%"] = screenshotUrl;
                statsRowBuilder.ReplaceStat("%screenshot%", screenshotUrl);

                StatsRow currentStats = statsRowBuilder.Build();

                await SaveToSpreadsheet(currentStats, hash);
            }
            catch(Exception e)
            {
                System.Console.WriteLine(e.Message);
                System.Console.WriteLine(e.StackTrace);

                Stash(stats);
            }
            
            this.statsSaved = true;
        }

        private void BackUpScreenshot()
        {
            var oldScreenshotPath = ScreenshotPath;
            ScreenshotPath = Path.Combine(stashPath, Path.GetFileName(oldScreenshotPath));

            if(!Directory.Exists(stashPath))
                Directory.CreateDirectory(stashPath);

            File.Copy(oldScreenshotPath, ScreenshotPath);
        }

        private async Task<string> UploadScreenshot()
        {
            byte[] screenshot = File.ReadAllBytes(ScreenshotPath);            

            GoogleApi.GoogleApi googleApi = GoogleApi.GoogleApi.GetInstance();
            string uploadToken = await googleApi.UploadToGooglePhotos(screenshot);            
            await googleApi.CreateMediaItemInGooglePhotos(Path.GetFileName(ScreenshotPath), uploadToken);

            // TODO: Add to album, share it and archive it

            File.Delete(ScreenshotPath);
            return "https://aniceimage/";
        }

        private async Task SaveToSpreadsheet(StatsRow currentStats, string songHash)
        {
            GoogleSpreadsheet spreadsheet = GoogleSpreadsheet.GetInstance();
            
            string playerName = MyGlobalVariables.GetInstance().CHPlayers[0].PlayerProfile.playerName;
            await spreadsheet.Init(UserConfig.GoogleUserCredentials, PluginInfo.PLUGIN_NAME, playerName);

            FindRowResult findRowResult = await spreadsheet.FindRow(new Dictionary<string, object> () { { "%hash%", songHash } });
            if(findRowResult.RowData == null)
            {
                System.Console.WriteLine("New Song! – Adding");
                spreadsheet.AppendRow(currentStats.RowData);
            }
            else
            {
                System.Console.WriteLine("Existing Song – Checking");
                StatsRow storedStats = new StatsRow(findRowResult.RowData);
                if(storedStats.CompareTo(currentStats) > 0)
                {
                    System.Console.WriteLine("Better stats! – Updating");
                    spreadsheet.UpdateRow(findRowResult.Index, currentStats.RowData);
                }
                else
                    System.Console.WriteLine("No improvement :( – Leaving");
            }
        }

        private void Stash(Dictionary<string, object> stats)
        {
            // JsonConvert.SerializeObject(stats);
            throw new NotImplementedException();
        }

        public void Reset()
        {
            this.statsSaved = false;
        }
    }
}