﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenQA;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;
using System.Xml;
using System.Xml.XPath;
using OpenQA.Selenium.PhantomJS;
using System.Net;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Collections.Specialized;

namespace Shatulsky_Farm {
    public partial class MainForm : Form {
        public MainForm() {
            InitializeComponent();
        }

        private async void StartButton_Click(object sender, EventArgs e) {
            BlockAll();

            #region Обнуление Database
            await Task.Run(() => {
                Database.BOT_LIST = new List<Bot>();
                Database.BOTS_LOADING = new List<bool>();
                Database.WASTED_MONEY = 0;
                Database.ALL_GAMES_LIST = new List<Game>();
            });
            #endregion

            #region Загрузка VDS
            var VDSs = ServersRichTextBox.Text.Split('\n').ToList();
            #region удалить пустые строки
            for (int i = 0; i < VDSs.Count; i++) {
                if (VDSs[i] == "" || VDSs[i] == "\n")
                    VDSs.RemoveAt(i--);
            }
            #endregion

            for (int i = 0; i < VDSs.Count; i++) {
                var VDS = VDSs[i];

                if (VDS != string.Empty) {
#pragma warning disable CS4014 
                    Task.Run(() => {
                        AddLog($"{VDS} - загрузка ботов начата");
                        Bot.AllBotsToDatabase(VDS);
                        Database.BOTS_LOADING.Add(true);
                        AddLog($"{VDS} - загрузка ботов завершена");
                    });
#pragma warning restore CS4014 
                }
            }

            await Task.Run(() => {
                bool done = false;
                while (!done) {
                    if (Database.BOTS_LOADING.Count == VDSs.Count)
                        break;
                }
            });
            #endregion


            await Task.Run(async () => {

                #region Обработка Json каталога
                Program.GetForm.MyMainForm.AddLog("Загрузка списка всех доступных игр");
                var response = Request.getResponse("http://shamanovski.pythonanywhere.com/catalogue");
                var json = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(response);
                foreach (var item in json) {
                    string appid = item.Path;
                    string lequeshop_id = item.Value.lequeshop_id.Value;
                    double price = item.Value.price.Value;
                    double amount = (item.Value.amount.Value.ToString() == "N/A") ? 0 : item.Value.amount.Value;
                    string store = item.Value.store.Value;
                    string game_name = item.Value.game_name.Value;
                    Database.ALL_GAMES_LIST.Add(new Game(appid, lequeshop_id, price, amount, store, game_name));
                }
                #endregion

                #region Поиск нужных игр
                foreach (var game in Database.ALL_GAMES_LIST) {
                    foreach (var bot in Database.BOT_LIST) {
                        if (bot.gamesNeed.Contains(game.appid))
                            game.count += 1;
                    }
                }

                double maxGamePrice = double.Parse(Program.GetForm.MyMainForm.MaxGameCostBox.Text.Replace('.', ','));
                for (int i = 0; i < Database.ALL_GAMES_LIST.Count; i++) {//удаляем из списка игры которые не нужны и которые дороже разрешонного
                    var game = Database.ALL_GAMES_LIST[i];
                    if (game.count == 0 || game.price > maxGamePrice || Database.BLACKLIST.Contains(game.appid) || game.store.Contains("akens.ru")) {
                        Database.ALL_GAMES_LIST.Remove(game);
                        i--;
                    }
                }

                Database.ALL_GAMES_LIST.Sort(); //сортируем от минимальной цены
                #endregion

                #region Покупка игр
                Program.GetForm.MyMainForm.AddLog($"Найдено {Database.ALL_GAMES_LIST.Count()} игр удовлетворяющих условию (<={Program.GetForm.MyMainForm.MaxGameCostBox.Text})");
                Program.GetForm.MyMainForm.AddLog("Начата покупка игр");

                foreach (var game in Database.ALL_GAMES_LIST) {

                    #region Пост запрос в магазин
                    var postData = "email=" + Program.GetForm.MyMainForm.EmailBox.Text.Replace("@", "%40");
                    if (game.count < game.amount) game.count = (int)game.amount; //если ключей не хватаел то купить сколько осталось 
                    postData += "&count=" + game.count;
                    postData += "&type=" + game.lequeshop_id;
                    postData += "&forms=%7B%7D&fund=4";
                    postData += "&copupon=";
                    var order = Request.POST(game.store + "/order", postData);

                    var jsonOrder = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(order);
                    #endregion

                    #region Данные заказа
                    var allPrice = jsonOrder.price.Value.Split(new[] { " QIWI" }, StringSplitOptions.None)[0];
                    var reciever = jsonOrder.fund.Value.Split('>')[1].Split('<')[0];
                    var comment = jsonOrder.bill.Value.Split('>')[1].Split('<')[0];
                    var buyLink = jsonOrder.check_url.Value.Replace("\\", "");
                    var count = jsonOrder.count.Value;
                    var appid = game.appid;
                    #endregion

                    #region Проверки
                    if (Database.WASTED_MONEY + allPrice > double.Parse(Program.GetForm.MyMainForm.MaxMoneyBox.Text)) { //заканчиваем цикл если достигли лимит по деньгам
                        Program.GetForm.MyMainForm.AddLog($"Достигнуто ограничение на покупку. Потрачено {Database.WASTED_MONEY}");
                        break;
                    }

                    var oneItemPrice = allPrice / count;
                    if (oneItemPrice < double.Parse(Program.GetForm.MyMainForm.MaxGameCostBox.Text.Replace('.', ','))) continue; //пропускаем элемент если его цена увеличилась выше допустимой
                    #endregion

                    #region Оплата
                    Program.GetForm.MyMainForm.AddLog($"Покупка {count} игр {game.game_name} ({appid}) по {Math.Round(oneItemPrice, 2)} на сумму {allPrice}");
                    var totalPrice = allPrice.ToString().Replace(',', '.');
                    Qiwi qiwiAccount = new Qiwi(Program.GetForm.MyMainForm.QiwiTokenBox.Text);
                    var paymentDone = await qiwiAccount.SendMoneyToWallet(reciever, totalPrice, comment);
                    if (!paymentDone) throw new Exception($"Не удалось оплатить {reciever} {comment} {appid} {totalPrice} руб. {buyLink}");
                    File.WriteAllText("buylinks.txt", $"{DateTime.Now} - {buyLink}");
                    Database.WASTED_MONEY += allPrice;
                    UpdateWastedMoney();
                    Program.GetForm.MyMainForm.AddLog($"Оплачено {totalPrice} руб, на номер {reciever}");
                    Thread.Sleep(5000);
                    #endregion

                    #region Загрузка файла
                    var downloadLink = "";
                    try { File.Delete("downloaded.txt"); } catch { };
                    //var fileDownloaded = Request.DownloadFile(downloadLink, Browser, "downloaded.txt");
                    //if (!fileDownloaded) throw new Exception($"Не удалось скачать файл {downloadLink}");
                    Thread.Sleep(1000);
                    var fileName = $"{appid} {game.game_name} - {DateTime.Now}";
                    fileName = fileName.Replace('.', '-');
                    fileName = fileName.Replace(':', '-');
                    Directory.CreateDirectory("keys");
                    File.Move("downloaded.txt", $"keys\\{fileName}.txt");
                    Program.GetForm.MyMainForm.AddLog($"Файл {fileName}.txt сохранен.");
                    Thread.Sleep(1000);
                    #endregion

                    #region Активация ключей
                    var keysList = File.ReadAllLines($"keys\\{fileName}.txt");
                    Program.GetForm.MyMainForm.AddLog($"Активация {keysList.Count()} ключей {game.game_name} ({appid})");
                    foreach (var line in keysList) {
                        foreach (var bot in Database.BOT_LIST) {
                            if (bot.gamesNeed.Contains(appid)) {
                                Regex regex = new Regex(@"\w{5}-\w{5}-\w{5}");
                                var key = regex.Match(line);
                                var command = $"http://{bot.vds}/IPC?command=";
                                command += $"!redeem^ {bot.login} SD,SF {key}";
                                var responsee = Request.getResponse(command);
                                File.AppendAllText($"responses.txt", $"\n{DateTime.Now} {bot.vds} {bot.login} {appid} {downloadLink} - {responsee}");

                                if (response.Contains("Timeout")) {
                                    Thread.Sleep(10000);
                                    var botResponse = Request.getResponse($"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={Program.GetForm.MyMainForm.ApikeyBox.Text}&steamid={bot.steamID}&format=json");
                                    if (botResponse.Contains(appid))
                                        response += "Ложный таймаут. OK/NoDetail";
                                }

                                if (response.Contains("OK/NoDetail") == false) {
                                    Program.GetForm.MyMainForm.AddLog($"Ошибка при активации ключей для {bot.vds},{bot.login},{key},{response.Replace('\r', ' ').Replace('\n', ' ')}");
                                    File.AppendAllText("UNUSEDKEYS.TXT", $"{bot.vds},{bot.login},{key},{response.Replace('\r', ' ').Replace('\n', ' ')}\n");
                                }
                                else {
                                    bot.gamesNeed.Remove(appid);
                                }
                                break;
                            }
                        }
                    }
                    Program.GetForm.MyMainForm.AddLog($"Ожидание 30 секунд до следующей покупки");
                    Thread.Sleep(30000);
                    Program.GetForm.MyMainForm.AddLog($"-----------------------------------");
                }
                #endregion
            });
            
            UnblockAll();
        }

        public void IncreaseBotsCount() {
            if (InvokeRequired)
                Invoke((Action)IncreaseBotsCount);
            else {
                Program.GetForm.MyMainForm.BotsLoadedCountLable.Text = (Database.BOT_LIST.Count + 1).ToString();
            }
        }
        public void UpdateWastedMoney() {
            if (InvokeRequired)
                Invoke((Action)UpdateWastedMoney);
            else {
                Program.GetForm.MyMainForm.WastedManeyCountLable.Text = Database.WASTED_MONEY.ToString();
            }
        }
        public void AddLog(string text) {
            if (InvokeRequired)
                Invoke((Action<string>)AddLog, text);
            else {
                Program.GetForm.MyMainForm.LogBox.AppendText(DateTime.Now + " - " + text + "\n");
                File.AppendAllText("log.txt", "\n" + text);
            }
        }
        public void BlockAll() {
            if (InvokeRequired)
                Invoke((Action)BlockAll);
            Program.GetForm.MyMainForm.groupBox1.Enabled = false;
            Program.GetForm.MyMainForm.groupBox2.Enabled = false;
            Program.GetForm.MyMainForm.BuyGamesButton.Enabled = false;
            Program.GetForm.MyMainForm.ActivateKeysButton.Enabled = false;
            Program.GetForm.MyMainForm.ActivateUnusedKeysButton.Enabled = false;
            Program.GetForm.MyMainForm.QIWIGroupBox.Enabled = false;
            Program.GetForm.MyMainForm.QIWIStartButton.Enabled = false;
            Program.GetForm.MyMainForm.QIWILoginsBox.Enabled = false;

        }
        public void UnblockAll() {
            if (InvokeRequired)
                Invoke((Action)UnblockAll);
            Program.GetForm.MyMainForm.groupBox1.Enabled = true;
            Program.GetForm.MyMainForm.groupBox2.Enabled = true;
            Program.GetForm.MyMainForm.BuyGamesButton.Enabled = true;
            Program.GetForm.MyMainForm.ActivateKeysButton.Enabled = true;
            Program.GetForm.MyMainForm.ActivateUnusedKeysButton.Enabled = true;
            Program.GetForm.MyMainForm.QIWIGroupBox.Enabled = true;
            Program.GetForm.MyMainForm.QIWIStartButton.Enabled = true;
            Program.GetForm.MyMainForm.QIWILoginsBox.Enabled = true;

        }
        private class DescendingComparer : IComparer<string> {
            int IComparer<string>.Compare(string a, string b) {
                return StringComparer.InvariantCulture.Compare(b, a);
            }
        }

        private void LogBox_TextChanged(object sender, EventArgs e) {
            LogBox.SelectionStart = LogBox.TextLength;
            LogBox.ScrollToCaret();
        }

        private void LootButton_Click(object sender, EventArgs e) {
            var unusedKeys = File.ReadAllLines("UNUSEDKEYS.TXT");
            foreach (var line in unusedKeys) {
                var data = line.Split(',');
                //{ bot.vds},{ bot.login},{ key},{ response.Replace('\r', ' ').Replace('\n', ' ')}
                var vds = data[0];
                var login = data[1];
                var key = data[1];
                var command = $"http://{vds}/IPC?command=!redeem {login} {key}";
                var response = Request.getResponse(command);
                Program.GetForm.MyMainForm.AddLog(response);
                if (response.Contains("OK/NoDetail") == false || response.Contains("RateLimited")) {
                    File.WriteAllText("UNUSEDKEYS.TXT", $"{vds},{login},{key},{response.Replace('\r', ' ').Replace('\n', ' ')}");
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e) {
            LogBox.Text = $"Программа запущена {System.DateTime.Now}\n";
            var settings = File.ReadAllText("settings.txt");
            var json = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(settings);
            ApikeyBox.Text = json.SteamAPI;
            KeysShopKey.Text = json.SteamkeysAPI;
            GotoCatalogBox.Text = json.GoToCatalogAPI;
            MaxGameCostBox.Text = json.MaxGameCost;
            MaxMoneyBox.Text = json.MaxMoneySpent;
            EmailBox.Text = json.Email;
            QiwiTokenBox.Text = json.QiwiToken;
            QiwiTokenBox2.Text = json.QiwiToken;
            for (int i = 0; i < json.VDSs.Count; i++) {
                ServersRichTextBox.AppendText(json.VDSs[i].Value + "\n");
            }
            Database.BLACKLIST = new List<string>();
            for (int i = 0; i < json.BlacklistAppids.Count; i++) {
                Database.BLACKLIST.Add(json.BlacklistAppids[i].Value);
            }
        }

        private async void ActivateKeysButton_Click(object sender, EventArgs e) {
            BlockAll();

            #region Обнуление Database
            await Task.Run(() => {
                Database.BOT_LIST = new List<Bot>();
                Database.BOTS_LOADING = new List<bool>();
                Database.WASTED_MONEY = 0;
                Database.ALL_GAMES_LIST = new List<Game>();
            });
            #endregion

            #region Загрузка VDS
            var VDSs = ServersRichTextBox.Text.Split('\n').ToList();
            #region удалить пустые строки
            for (int i = 0; i < VDSs.Count; i++) {
                if (VDSs[i] == "" || VDSs[i] == "\n")
                    VDSs.RemoveAt(i--);
            }
            #endregion

            for (int i = 0; i < VDSs.Count; i++) {
                var VDS = VDSs[i];

                if (VDS != string.Empty) {
#pragma warning disable CS4014
                    Task.Run(() => {
                        AddLog($"{VDS} - загрузка ботов начата");
                        Bot.AllBotsToDatabase(VDS);
                        Database.BOTS_LOADING.Add(true);
                        AddLog($"{VDS} - загрузка ботов завершена");
                    });
#pragma warning restore CS4014
                }
            }

            await Task.Run(() => {
                bool done = false;
                while (!done) {
                    if (Database.BOTS_LOADING.Count == VDSs.Count)
                        break;
                }
            });
            #endregion

            #region Активация
            Directory.CreateDirectory("activate");
            var files = Directory.GetFiles("activate");
            foreach (var file in files) {
                var appid = file.Split('\\')[1].Split('.')[0];
                var keys = File.ReadAllLines(file);
                for (int i = 0; i < keys.Count(); i++) {
                    if (keys[i] != String.Empty) {
                        foreach (var bot in Database.BOT_LIST) {
                            if (bot.gamesNeed.Contains(appid)) {

                                Regex regex = new Regex(@"\w{5}-\w{5}-\w{5}");
                                var key = regex.Match(keys[i]);
                                var command = $"http://{bot.vds}/IPC?command=";
                                command += $"!redeem {bot.login} {key}";
                                var response = Request.getResponse(command);
                                Program.GetForm.MyMainForm.AddLog($"{bot.vds} - {response}\n");
                                File.AppendAllText($"responses.txt", $"\n{DateTime.Now} {bot.vds} {bot.login} {appid} - {response}");
                                if (response.Contains("Timeout")) {
                                    Thread.Sleep(10000);
                                    var botResponse = Request.getResponse($"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={Program.GetForm.MyMainForm.ApikeyBox.Text}&steamid={bot.steamID}&format=json");
                                    if (botResponse.Contains(appid))
                                        response += "Ложный таймаут. OK/NoDetail";
                                }
                                if (response.Contains("OK/NoDetail") == false || response.Contains("RateLimited")) {
                                    Program.GetForm.MyMainForm.AddLog($"Ошибка при активации ключей для {bot.vds} из {file}.txt\n{response}");
                                    //Thread.Sleep(Timeout.Infinite);
                                }
                                else {
                                    keys[i] = string.Empty;
                                    File.WriteAllText(file, keys.ToString());
                                }
                                break;
                            }
                        }
                    }

                }
            }
            #endregion

            UnblockAll();
        }

        private async void button1_Click(object sender, EventArgs e) {
            BlockAll();
            await Task.Run(async () => {
                string text = "";
                Invoke((Action)(() => {
                    text = Program.GetForm.MyMainForm.QIWILoginsBox.Text.Clone().ToString();
                }));

                var inputBots = text.Replace("\r", "").Split('\n');

                int processStatus = 0;
                Qiwi qiwiAccount = new Qiwi(Program.GetForm.MyMainForm.QiwiTokenBox2.Text);
                var money = Program.GetForm.MyMainForm.QIWIDonateBox.Text.Replace(',', '.');
                foreach (var bot in inputBots) {
                    if (bot != String.Empty) {
                        var paymentDone = await qiwiAccount.SendMoneyToSteam(bot, money);
                        if (!paymentDone) {
                            Program.GetForm.MyMainForm.AddLog($"[{++processStatus}/{inputBots.Count()}] {bot} ОШИБКА ПОПОЛНЕНИЯ!");
                            break;
                        }
                        Program.GetForm.MyMainForm.AddLog($"[{++processStatus}/{inputBots.Count()}] {bot} пополнение на сумму {money} руб успешно проведено.");
                        Thread.Sleep(1111);
                    }
                }
            });
            UnblockAll();
        }
    }
}
#endregion
