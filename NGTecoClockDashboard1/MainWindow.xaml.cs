using NGTecoClockDashboard;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using zkemkeeper;
using AutoUpdaterDotNET;

namespace NGTecoClockDashboard1
{
    public class AttendanceRecord
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public DateTime Timestamp { get; set; }
        public string VerifyMode { get; set; }
        public string InOutMode { get; set; }
    }

    public class UserInfoModel
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public string Privilege { get; set; }
        public string Status { get; set; }
    }

    public class PayrollSummaryModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public int DaysWorked { get; set; }
        public string TotalHoursFormatted { get; set; }
        public double TotalHoursDecimal { get; set; }
    }

    public partial class MainWindow : Window
    {
        public CZKEM axCZKEM1 = new CZKEM();
        private const string ClockIp = "192.168.1.201";
        private const int ClockPort = 4370;

        // File path to store local ID -> Name mappings
        private readonly string mappingFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_mappings.json");
        private Dictionary<string, string> localUserMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<AttendanceRecord> currentLogs = new List<AttendanceRecord>();

        public MainWindow()
        {
            InitializeComponent();
            LoadLocalUserMappings();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Are you sure you want to log out?",
                "Confirm Logout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 1. Safely disconnect clock SDK if connected
                try { axCZKEM1.Disconnect(); } catch { }

                // 2. Configure AutoUpdater options
                AutoUpdater.RunUpdateAsAdmin = false;
                AutoUpdater.ShowRemindLaterButton = true;

                // 3. Check GitHub for updates
                AutoUpdater.Start("https://raw.githubusercontent.com/Lifereaper/AQ-time-tracker/main/update.xml");

                // 4. Return to Login Screen
                LoginWindow loginWindow = new LoginWindow();
                loginWindow.Show();
                Application.Current.MainWindow = loginWindow;
                this.Close();
            }
        }

        // Load saved ID->Name mappings from JSON
        private void LoadLocalUserMappings()
        {
            try
            {
                if (File.Exists(mappingFilePath))
                {
                    string json = File.ReadAllText(mappingFilePath);
                    localUserMap = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                lblStatus.Content = $"Error loading local mapping: {ex.Message}";
            }
        }

        // Save ID->Name mappings to JSON
        private void SaveLocalUserMappings()
        {
            try
            {
                string json = JsonSerializer.Serialize(localUserMap, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(mappingFilePath, json);
            }
            catch (Exception ex)
            {
                lblStatus.Content = $"Error saving local mapping: {ex.Message}";
            }
        }

        private void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            FetchLogsFromClock();
        }

        private List<AttendanceRecord> FetchLogsFromClock()
        {
            axCZKEM1.SetCommPassword(123);

            if (axCZKEM1.Connect_Net(ClockIp, ClockPort))
            {
                lblStatus.Content = "Connected to clock. Fetching logs...";
                int machineNumber = 1;

                // Sync device user names into local map
                FetchUserNamesFromClock(machineNumber);

                axCZKEM1.ReadGeneralLogData(machineNumber);

                currentLogs = new List<AttendanceRecord>();
                string enrollNumber = "";
                int verifyMode = 0, inOutMode = 0, year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0, workCode = 0;

                while (axCZKEM1.SSR_GetGeneralLogData(machineNumber, out enrollNumber,
                       out verifyMode, out inOutMode, out year, out month, out day,
                       out hour, out minute, out second, ref workCode))
                {
                    DateTime punchTime;
                    try { punchTime = new DateTime(year, month, day, hour, minute, second); }
                    catch { punchTime = DateTime.MinValue; }

                    string cleanId = SanitizeString(enrollNumber);

                    if (IsValidUserId(cleanId))
                    {
                        currentLogs.Add(new AttendanceRecord
                        {
                            UserId = cleanId,
                            UserName = ResolveUserName(cleanId),
                            Timestamp = punchTime,
                            VerifyMode = FormatVerifyMode(verifyMode),
                            InOutMode = FormatInOutMode(inOutMode)
                        });
                    }
                }

                // --- DATE & NAME FILTERING LOGIC ---
                DateTime? startDate = dpStartDate.SelectedDate;
                DateTime? endDate = dpEndDate.SelectedDate;
                string searchQuery = txtSearchName.Text?.Trim().ToLower() ?? "";

                // Adjust the end date to include the entire day (up to 23:59:59)
                if (endDate.HasValue)
                {
                    endDate = endDate.Value.Date.AddDays(1).AddTicks(-1);
                }

                // Filter the logs using LINQ (Date range + Name/ID search query)
                var filteredLogs = currentLogs.Where(log =>
                    (!startDate.HasValue || log.Timestamp >= startDate.Value) &&
                    (!endDate.HasValue || log.Timestamp <= endDate.Value) &&
                    (string.IsNullOrEmpty(searchQuery) ||
                     log.UserName.ToLower().Contains(searchQuery) ||
                     log.UserId.Contains(searchQuery))
                ).ToList();

                dgLogs.ItemsSource = null;
                dgLogs.ItemsSource = filteredLogs;
                lblStatus.Content = $"Successfully loaded {filteredLogs.Count} punch records.";
                // ---------------------------------

                axCZKEM1.Disconnect();
            }
            else
            {
                int errorCode = 0;
                axCZKEM1.GetLastError(ref errorCode);
                lblStatus.Content = $"Connection failed. Error code: {errorCode}";
            }

            return currentLogs;
        }

        private void BtnCalculatePayroll_Click(object sender, RoutedEventArgs e)
        {
            // Fetch fresh logs from clock to ensure payroll is up to date
            List<AttendanceRecord> allLogs = FetchLogsFromClock();

            if (allLogs == null || allLogs.Count == 0)
            {
                lblStatus.Content = "No logs available for payroll calculation.";
                return;
            }

            DateTime? startDate = dpPayrollStart.SelectedDate;
            DateTime? endDate = dpPayrollEnd.SelectedDate;
            string searchQuery = txtPayrollSearch.Text?.Trim().ToLower() ?? "";

            // Adjust end date to cover the full end day
            if (endDate.HasValue)
            {
                endDate = endDate.Value.Date.AddDays(1).AddTicks(-1);
            }

            // Filter logs by date range for payroll week/period
            var filteredLogs = allLogs.Where(log =>
                (!startDate.HasValue || log.Timestamp >= startDate.Value) &&
                (!endDate.HasValue || log.Timestamp <= endDate.Value)
            ).ToList();

            // Group by User ID to calculate duration between check-in and check-out pairs
            var userGroups = filteredLogs.GroupBy(l => l.UserId);
            var payrollList = new List<PayrollSummaryModel>();

            foreach (var group in userGroups)
            {
                string userId = group.Key;
                string userName = group.First().UserName;

                // Apply name/ID search filter
                if (!string.IsNullOrEmpty(searchQuery) &&
                    !userName.ToLower().Contains(searchQuery) &&
                    !userId.Contains(searchQuery))
                {
                    continue;
                }

                var sortedPunches = group.OrderBy(p => p.Timestamp).ToList();
                double totalMinutes = 0;
                DateTime? lastCheckIn = null;

                foreach (var punch in sortedPunches)
                {
                    if (punch.InOutMode.Contains("In"))
                    {
                        lastCheckIn = punch.Timestamp;
                    }
                    else if (punch.InOutMode.Contains("Out") && lastCheckIn.HasValue)
                    {
                        if (punch.Timestamp > lastCheckIn.Value)
                        {
                            totalMinutes += (punch.Timestamp - lastCheckIn.Value).TotalMinutes;
                        }
                        lastCheckIn = null; // Reset pair
                    }
                }

                double totalHours = totalMinutes / 60.0;
                int daysWorked = group.Select(p => p.Timestamp.Date).Distinct().Count();

                payrollList.Add(new PayrollSummaryModel
                {
                    UserId = userId,
                    UserName = userName,
                    DaysWorked = daysWorked,
                    TotalHoursFormatted = $"{Math.Floor(totalHours)} hrs {Math.Round((totalHours - Math.Floor(totalHours)) * 60)} mins",
                    TotalHoursDecimal = Math.Round(totalHours, 2)
                });
            }

            dgPayroll.ItemsSource = null;
            dgPayroll.ItemsSource = payrollList.OrderBy(p => p.UserName).ToList();
            lblStatus.Content = $"Payroll calculated for {payrollList.Count} employee(s).";
        }

        private void BtnLoadUsers_Click(object sender, RoutedEventArgs e)
        {
            axCZKEM1.SetCommPassword(123);

            if (axCZKEM1.Connect_Net(ClockIp, ClockPort))
            {
                lblStatus.Content = "Fetching users from device...";
                int machineNumber = 1;
                Dictionary<string, UserInfoModel> userDict = new Dictionary<string, UserInfoModel>();

                // Pass 1: Query device memory buffer
                if (axCZKEM1.ReadAllUserID(machineNumber))
                {
                    string sEnrollNumber = "", sName = "", sPassword = "";
                    int iPrivilege = 0;
                    bool bEnabled = false;

                    while (axCZKEM1.SSR_GetAllUserInfo(machineNumber, out sEnrollNumber, out sName, out sPassword, out iPrivilege, out bEnabled))
                    {
                        string cleanId = SanitizeString(sEnrollNumber);
                        string cleanName = SanitizeString(sName);

                        if (IsValidUserId(cleanId))
                        {
                            // Only update local map from clock if a custom local mapping does NOT already exist
                            if (!string.IsNullOrEmpty(cleanName) && !localUserMap.ContainsKey(cleanId))
                            {
                                localUserMap[cleanId] = cleanName;
                            }

                            userDict[cleanId] = new UserInfoModel
                            {
                                UserId = cleanId,
                                Name = ResolveUserName(cleanId),
                                Privilege = iPrivilege == 3 ? "Admin" : "Standard User",
                                Status = bEnabled ? "Enabled" : "Disabled"
                            };
                        }
                    }
                }

                // Pass 2: Query attendance log buffer for missing IDs
                axCZKEM1.ReadGeneralLogData(machineNumber);
                string logEnrollNumber = "";
                int verifyMode = 0, inOutMode = 0, year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0, workCode = 0;

                while (axCZKEM1.SSR_GetGeneralLogData(machineNumber, out logEnrollNumber,
                       out verifyMode, out inOutMode, out year, out month, out day,
                       out hour, out minute, out second, ref workCode))
                {
                    string cleanLogId = SanitizeString(logEnrollNumber);

                    if (IsValidUserId(cleanLogId) && !userDict.ContainsKey(cleanLogId))
                    {
                        userDict[cleanLogId] = new UserInfoModel
                        {
                            UserId = cleanLogId,
                            Name = ResolveUserName(cleanLogId),
                            Privilege = "Standard User",
                            Status = "Enabled"
                        };
                    }
                }

                // Pass 3: Include locally mapped users even if they haven't punched yet
                foreach (var kvp in localUserMap)
                {
                    if (IsValidUserId(kvp.Key) && !userDict.ContainsKey(kvp.Key))
                    {
                        userDict[kvp.Key] = new UserInfoModel
                        {
                            UserId = kvp.Key,
                            Name = kvp.Value,
                            Privilege = "Standard User",
                            Status = "App Mapping Only"
                        };
                    }
                }

                SaveLocalUserMappings();

                List<UserInfoModel> usersList = userDict.Values
                    .Where(u => IsValidUserId(u.UserId))
                    .OrderBy(u => int.TryParse(u.UserId, out int id) ? id : 99999)
                    .ToList();

                dgUsers.ItemsSource = null;
                dgUsers.ItemsSource = usersList;
                lblStatus.Content = $"Loaded {usersList.Count} user accounts.";
                axCZKEM1.Disconnect();
            }
            else
            {
                lblStatus.Content = "Clock disconnected. Loading local user mappings...";

                // Offline mode: load from local map
                List<UserInfoModel> usersList = localUserMap
                    .Where(kvp => IsValidUserId(kvp.Key))
                    .Select(kvp => new UserInfoModel
                    {
                        UserId = kvp.Key,
                        Name = kvp.Value,
                        Privilege = "Standard User",
                        Status = "Offline Mode"
                    })
                    .OrderBy(u => int.TryParse(u.UserId, out int id) ? id : 99999)
                    .ToList();

                dgUsers.ItemsSource = null;
                dgUsers.ItemsSource = usersList;
            }
        }

        private void BtnAddUser_Click(object sender, RoutedEventArgs e)
        {
            string newId = txtAddUserId.Text.Trim();
            string newName = txtAddUserName.Text.Trim();

            if (string.IsNullOrEmpty(newId) || !IsValidUserId(newId))
            {
                MessageBox.Show("Please enter a valid numeric User ID.", "Invalid ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("Please enter a Name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Save mapping locally for matching incoming clock logs
            localUserMap[newId] = newName;
            SaveLocalUserMappings();

            txtAddUserId.Clear();
            txtAddUserName.Clear();

            lblStatus.Content = $"Saved mapping: ID {newId} = {newName}";
            MessageBox.Show($"Mapped ID {newId} to '{newName}' successfully!\n\nWhen this ID punches on the NGTeco clock app or physical device, logs will display as '{newName}'.", "Mapping Saved", MessageBoxButton.OK, MessageBoxImage.Information);

            // Refresh table
            BtnLoadUsers_Click(null, null);
        }

        private void FetchUserNamesFromClock(int machineNumber)
        {
            if (axCZKEM1.ReadAllUserID(machineNumber))
            {
                string sEnrollNumber = "", sName = "", sPassword = "";
                int iPrivilege = 0;
                bool bEnabled = false;

                while (axCZKEM1.SSR_GetAllUserInfo(machineNumber, out sEnrollNumber, out sName, out sPassword, out iPrivilege, out bEnabled))
                {
                    string cleanId = SanitizeString(sEnrollNumber);
                    string cleanName = SanitizeString(sName);

                    if (IsValidUserId(cleanId) && !string.IsNullOrEmpty(cleanName))
                    {
                        // Only save if a local custom mapping doesn't already exist
                        if (!localUserMap.ContainsKey(cleanId))
                        {
                            localUserMap[cleanId] = cleanName;
                        }
                    }
                }
                SaveLocalUserMappings();
            }
        }

        private string ResolveUserName(string enrollNumber)
        {
            if (string.IsNullOrEmpty(enrollNumber)) return "Unknown";

            if (localUserMap.TryGetValue(enrollNumber, out string name) && !string.IsNullOrEmpty(name))
            {
                return name;
            }

            return "Unnamed User";
        }

        private string SanitizeString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return raw.Replace("\0", "").Replace("\r", "").Replace("\n", "").Trim();
        }

        private bool IsValidUserId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && id != "0" && id.All(char.IsDigit);
        }

        private string FormatVerifyMode(int mode)
        {
            switch (mode)
            {
                case 0: return "Password";
                case 1: return "Fingerprint";
                case 2: return "Card";
                case 15: return "Face";
                default: return $"Code ({mode})";
            }
        }

        private string FormatInOutMode(int mode)
        {
            switch (mode)
            {
                case 0: return "Check-In";
                case 1: return "Check-Out";
                case 2: return "Break-Out";
                case 3: return "Break-In";
                case 4: return "OT-In";
                case 5: return "OT-Out";
                default: return $"Type {mode}";
            }
        }
    }
}