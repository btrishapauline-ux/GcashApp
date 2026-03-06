using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GCashSimulator
{
    enum TransactionType
    {
        CashIn, CashOut, SendMoney, Receive, BuyLoad,
        BankTransfer, GCredit, Refund
    }

    enum AccountStatus { Active, Locked, Suspended }

    class Transaction
    {
        public TransactionType Type { get; private set; }
        public decimal Amount { get; private set; }
        public DateTime Date { get; private set; }
        public decimal BalanceAfterTransaction { get; private set; }
        public string Description { get; private set; }
        public string ReferenceNo { get; private set; }

        public Transaction(TransactionType type, decimal amount, decimal balanceAfter, string description)
        {
            Type = type;
            Amount = amount;
            Date = DateTime.Now;
            BalanceAfterTransaction = balanceAfter;
            Description = description;
            ReferenceNo = GenerateRef();
        }

        private string GenerateRef()
        {
            return "GC" + DateTime.Now.ToString("yyMMdd") +
                   new Random().Next(100000, 999999).ToString();
        }

        public override string ToString()
        {
            string sign = (Type == TransactionType.SendMoney ||
                           Type == TransactionType.CashOut ||
                           Type == TransactionType.BuyLoad ||
                           Type == TransactionType.BankTransfer ) ? "-" : "+";

            return $"  {Date:MM/dd/yy HH:mm}  {ReferenceNo,-14}  {Type,-14}  {sign}{Amount,10:N2}  Bal: {BalanceAfterTransaction,10:N2}  {Description}";
        }
    }

    abstract class GCashAccount
    {
        private decimal _balance;
        private string _mpin;

        public string Name { get; private set; }
        public string Phone { get; private set; }
        public string Email { get; set; }
        public AccountStatus Status { get; set; }
        public bool IsVerified { get; protected set; }
        public decimal WalletLimit { get; protected set; }
        public decimal SendLimit { get; protected set; }

        public decimal CreditLine { get; protected set; }
        public decimal CreditUsed { get; set; }

        public List<Transaction> History { get; } = new List<Transaction>();

        public abstract string AccountType { get; }

        protected GCashAccount(string name, string phone, string mpin, string email)
        {
            Name = name;
            Phone = phone;
            _mpin = mpin;
            Email = email;
            _balance = 0m;
            Status = AccountStatus.Active;
            CreditUsed = 0m;
        }

        public decimal GetBalance() => _balance;
        protected void SetBalance(decimal val) => _balance = val;
        public void AdjustBalance(decimal delta) => _balance += delta;

        public bool ValidatePin(string input) => _mpin == input;
        public void ChangePin(string newPin) => _mpin = newPin;

        // ---- Abstract Methods ----
        public abstract decimal GetCashInLimit();
        public abstract decimal GetCashOutLimit();
        public abstract string GetAccountBenefits();

        // ---- Shared: Record Transaction ----
        protected void Record(TransactionType type, decimal amount, string desc)
        {
            History.Add(new Transaction(type, amount, _balance, desc));
        }

        // ---- Show Balance ----
        public void ShowWallet()
        {
            Console.WriteLine();
            Console.WriteLine($"  GCash Wallet — {AccountType}");
            Console.WriteLine($"  Name         : {Name}");
            Console.WriteLine($"  Phone        : {Phone}");
            Console.WriteLine($"  Wallet Bal   : P {_balance}");
            if (CreditLine > 0)
                Console.WriteLine($"  GCredit      : P {CreditLine - CreditUsed} available of P {CreditLine}");
        }

        // ---- Transaction History ----
        public void ShowHistory()
        {
            if (History.Count == 0) { Console.WriteLine("\n  No transactions yet."); return; }
            Console.WriteLine($"\n  {"Date",-14} {"Reference",-14} {"Type",-14} {"Amount",12} {"Balance",14} Description");
            Console.WriteLine("  " + new string('-', 100));
            int start = Math.Max(0, History.Count - 20); // show last 20
            for (int i = start; i < History.Count; i++)
                Console.WriteLine(History[i]);
            if (History.Count > 20)
                Console.WriteLine($"  ... and {History.Count - 20} older transaction(s).");
        }
    }

    /* ============================================================
                      BASIC ACCOUNT  (unverified)
     ============================================================*/
    class BasicAccount : GCashAccount
    {
        public override string AccountType => "Basic (Unverified)";
        public override decimal GetCashInLimit() => 8_000m;
        public override decimal GetCashOutLimit() => 5_000m;

        public BasicAccount(string name, string phone, string mpin, string email)
            : base(name, phone, mpin, email)
        {
            IsVerified = false;
            WalletLimit = 100_000m;
            SendLimit = 50_000m;
            CreditLine = 0m;
        }

        public override string GetAccountBenefits() =>
            "  Basic Account Limits:\n" +
            "  - Wallet Limit     : P100,000\n" +
            "  - Send Money       : P50,000/day\n" +
            "  - Cash In Limit    : P8,000/day\n" +
            "  - Cash Out Limit   : P5,000/day\n" +
            "  - No GCredit\n" +
            "  Tip: Verify your account to unlock higher limits!";
    }

    /* ============================================================
                          VERIFIED ACCOUNT
     ============================================================*/
    class VerifiedAccount : GCashAccount
    {
        public override string AccountType => "Fully Verified";
        public override decimal GetCashInLimit() => 100_000m;
        public override decimal GetCashOutLimit() => 50_000m;

        public VerifiedAccount(string name, string phone, string mpin, string email)
            : base(name, phone, mpin, email)
        {
            IsVerified = true;
            WalletLimit = 500_000m;
            SendLimit = 100_000m;
            CreditLine = 10_000m;
        }

        public override string GetAccountBenefits() =>
            "  Verified Account Limits:\n" +
            "  - Wallet Limit     : P500,000\n" +
            "  - Send Money       : P100,000/day\n" +
            "  - Cash In Limit    : P100,000/day\n" +
            "  - Cash Out Limit   : P50,000/day\n" +
            "  - GCredit Line     : P10,000" ;
    }

   


    /* ============================================================
                          GCASH SYSTEM CLASS
     ============================================================*/
    class GCashSystem
    {
        private Dictionary<string, GCashAccount> _accounts = new Dictionary<string, GCashAccount>();
        private Dictionary<string, int> _loginAttempts = new Dictionary<string, int>();
        public GCashAccount CurrentUser { get; private set; }

        // ---- Load Promos ----
        private static readonly Dictionary<string, (string desc, decimal price)> LoadPromos =
            new Dictionary<string, (string, decimal)>
        {
            { "1",  ("Regular Load P10",         10m)   },
            { "2",  ("Regular Load P50",         50m)   },
            { "3",  ("Regular Load P100",        100m)  },
            { "4",  ("GIGA99 — 5GB 7 days",      99m)   },
            { "5",  ("GIGA199 — 15GB 30 days",   199m)  },
            { "6",  ("GoSURF299 — 30GB 30 days", 299m)  },
        };

        /* ============================================================
                              REGISTRATION
         ============================================================*/
        public bool Register(string name, string phone, string mpin, string email, bool verified)
        {
            if (_accounts.ContainsKey(phone)) return false;

            GCashAccount acc = verified
                ? (GCashAccount)new VerifiedAccount(name, phone, mpin, email)
                : new BasicAccount(name, phone, mpin, email);

            _accounts[phone] = acc;
            _loginAttempts[phone] = 0;
            return true;
        }

        /* ============================================================
                    GCREDIT
        ============================================================*/
        public void UseGCredit(string purpose, decimal amount)
        {
            if (!CurrentUser.IsVerified)
                throw new InvalidOperationException("GCredit requires a Fully Verified account.");

            decimal available = CurrentUser.CreditLine - CurrentUser.CreditUsed;
            if (amount > available)
                throw new InvalidOperationException($"Insufficient GCredit. Available: P {available}");

            CurrentUser.CreditUsed += amount;
            CurrentUser.AdjustBalance(amount);
            CurrentUser.History.Add(new Transaction(TransactionType.GCredit, amount, CurrentUser.GetBalance(),
                $"GCredit Used — {purpose}"));
            Console.WriteLine($"\n GCredit of P {amount} used for {purpose}.");
            Console.WriteLine($"       GCredit Available: P {CurrentUser.CreditLine - CurrentUser.CreditUsed}");
        }

        public void PayGCredit(decimal amount)
        {
            if (CurrentUser.CreditUsed == 0)
                throw new InvalidOperationException("No outstanding GCredit balance.");
            EnsureSufficientBalance(amount);
            if (amount > CurrentUser.CreditUsed) amount = CurrentUser.CreditUsed;

            CurrentUser.AdjustBalance(-amount);
            CurrentUser.CreditUsed -= amount;
            CurrentUser.History.Add(new Transaction(TransactionType.GCredit, amount, CurrentUser.GetBalance(),
                "GCredit Payment"));
            Console.WriteLine($"\n Paid P {amount} to GCredit.");
            Console.WriteLine($"       GCredit Available: P {CurrentUser.CreditLine - CurrentUser.CreditUsed}");
        }
        /* ============================================================
                                     LOGIN
         ============================================================*/
        public string Login(string phone, string mpin)
        {
            if (!_accounts.ContainsKey(phone)) return "NOT_FOUND";
            var acc = _accounts[phone];
            if (acc.Status == AccountStatus.Locked) return "LOCKED";

            if (!acc.ValidatePin(mpin))
            {
                _loginAttempts[phone]++;
                if (_loginAttempts[phone] >= 3)
                {
                    acc.Status = AccountStatus.Locked;
                    return "LOCKED_NOW";
                }
                return $"WRONG_{3 - _loginAttempts[phone]}";
            }

            _loginAttempts[phone] = 0;
            CurrentUser = acc;
            return "OK";
        }

        public void Logout() => CurrentUser = null;

        /* ============================================================
                                    CASH IN
         ============================================================*/
        public void CashIn(decimal amount)
        {
            ValidateAmount(amount, 1m, CurrentUser.GetCashInLimit(), "Cash-in");
            if (CurrentUser.GetBalance() + amount > CurrentUser.WalletLimit)
                throw new InvalidOperationException($"Amount exceeds wallet limit of P {CurrentUser.WalletLimit}.");

            CurrentUser.AdjustBalance(amount);
            CurrentUser.History.Add(new Transaction(TransactionType.CashIn, amount, CurrentUser.GetBalance(),
                "Cash In via OTC/Bank"));
            Console.WriteLine($"\n Cash In successful!  Wallet Balance: P {CurrentUser.GetBalance()}");
        }

        /* ============================================================
                                  CASH OUT
         ============================================================*/
        public void CashOut(decimal amount)
        {
            ValidateAmount(amount, 1m, CurrentUser.GetCashOutLimit(), "Cash-out");
            EnsureSufficientBalance(amount);

            CurrentUser.AdjustBalance(-amount);
            CurrentUser.History.Add(new Transaction(TransactionType.CashOut, amount, CurrentUser.GetBalance(),
                "Cash Out via Partner Outlet"));
            Console.WriteLine($"\n Cash Out successful!  Wallet Balance: P {CurrentUser.GetBalance()}");
        }

        /* ============================================================
                                  SEND MONEY
         ============================================================*/
        public void SendMoney(string recipientPhone, decimal amount)
        {
            if (recipientPhone == CurrentUser.Phone)
                throw new InvalidOperationException("You cannot send money to yourself.");
            if (!_accounts.ContainsKey(recipientPhone))
                throw new InvalidOperationException("Recipient GCash number not found.");

            ValidateAmount(amount, 1m, CurrentUser.SendLimit, "Send Money");
            EnsureSufficientBalance(amount);

            var recipient = _accounts[recipientPhone];
            CurrentUser.AdjustBalance(-amount);
            recipient.AdjustBalance(amount);

            CurrentUser.History.Add(new Transaction(TransactionType.SendMoney, amount, CurrentUser.GetBalance(),
                $"Sent to {recipient.Name} ({recipientPhone})"));
            recipient.History.Add(new Transaction(TransactionType.Receive, amount, recipient.GetBalance(),
                $"Received from {CurrentUser.Name} ({CurrentUser.Phone})"));

            Console.WriteLine($"\n Sent P {amount} to {recipient.Name}!");
            Console.WriteLine($"       Your Balance: P {CurrentUser.GetBalance()}");
        }

        /* ============================================================
                                  BANK TRANSFER
         ============================================================*/
        public void BankTransfer(string bankName, string accountNo, decimal amount)
        {
            ValidateAmount(amount, 1m, 500_000m, "Bank Transfer");
            EnsureSufficientBalance(amount);

            CurrentUser.AdjustBalance(-amount);
            CurrentUser.History.Add(new Transaction(TransactionType.BankTransfer, amount, CurrentUser.GetBalance(),
                $"Transfer to {bankName} — Acct: {accountNo}"));
            Console.WriteLine($"\n P{amount:N2} sent to {bankName} ({accountNo}).");
            Console.WriteLine($"       Your Balance: P {CurrentUser.GetBalance()}");
        }

        /* ============================================================
                                  BUY LOAD
         ============================================================*/
        public Dictionary<string, (string desc, decimal price)> GetLoadPromos() => LoadPromos;

        public void BuyLoad(string targetPhone, string promoKey)
        {
            if (!LoadPromos.ContainsKey(promoKey))
                throw new ArgumentException("\nError: Invalid promo selected.");

            var (desc, price) = LoadPromos[promoKey];
            EnsureSufficientBalance(price);

            CurrentUser.AdjustBalance(-price);
            CurrentUser.History.Add(new Transaction(TransactionType.BuyLoad, price, CurrentUser.GetBalance(),
                $"Load: {desc} → {targetPhone}"));
            Console.WriteLine($"\n {desc} sent to {targetPhone}!");
            Console.WriteLine($"       Your Balance: P {CurrentUser.GetBalance()}");
        }

        /* ============================================================
                                  CHANGE MPIN
         ============================================================*/
        public void ChangeMpin(string newPin) => CurrentUser.ChangePin(newPin);

        /* ============================================================
                                      HELPERS
         ============================================================*/
        public bool AccountExists(string phone) => _accounts.ContainsKey(phone);

        private void ValidateAmount(decimal amount, decimal min, decimal max, string context)
        {
            if (amount < min)
                throw new ArgumentException($"{context}: Minimum amount is P {min}.");
            if (amount > max)
                throw new ArgumentException($"{context}: Maximum amount is P {max}.");
        }

        private void EnsureSufficientBalance(decimal amount)
        {
            if (CurrentUser.GetBalance() < amount)
                throw new InvalidOperationException(
                    $"Insufficient wallet balance. Current: P {CurrentUser.GetBalance()}");
        }
    }

   
    /* ============================================================
                              PROGRAM CLASS
     ============================================================ */
    class Program
    {
        static GCashSystem gcash = new GCashSystem();

        static void Main(string[] args)
        {
            MainMenu();
        }

        /* =========================================================
                                    MAIN MENU
         ========================================================= */
        static void MainMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("====== Welcome to Gcash ======");
                Console.WriteLine("  [1]  Register");
                Console.WriteLine("  [2]  Login");
                Console.WriteLine("  [3]  Exit");
                Console.WriteLine("--------------------");
                Console.Write("  Select: ");

                switch (Console.ReadLine()?.Trim())
                {
                    case "1": Register(); break;
                    case "2": Login(); break;
                    case "3":
                        Console.WriteLine("\n  Salamat sa GCash! Goodbye.");
                        return;
                    default: 
                        Console.WriteLine("\nError: Invalid option.");
                        Console.ReadKey();
                        break;
                }
            }
        }

        /* =========================================================
                                     REGISTER
         =========================================================*/
        static void Register()
        {
            Console.Clear();
            Console.WriteLine("====== REGISTER ======");

            string name = GetNonEmpty("  Full Name   : ");
            string phone = GetPhone("  Mobile No   : ");

            if (gcash.AccountExists(phone))
            { 
                Console.WriteLine ("Number already registered.");
                Console.ReadKey();
                return; 
            }

            string email = GetEmail("  Email       : ");
            string mpin = GetMpin("  Create MPIN (4 digits): ");

            Console.WriteLine("\n  Account Type:");
            Console.WriteLine("  [1] Basic (Unverified)");
            Console.WriteLine("  [2] Fully Verified");
            string accType = GetChoice("  Select: ", new[] { "1", "2" });
            bool verified = accType == "2";

            gcash.Register(name, phone, mpin, email, verified);

            Console.WriteLine(" Registration successful!");
            Console.WriteLine($"       Name  : {name}");
            Console.WriteLine($"       Phone : {phone}");
            Console.WriteLine($"       Type  : {(verified ? "Fully Verified" : "Basic")}");
            Console.ReadKey();
        }

        /* =========================================================
                                      LOGIN
         =========================================================*/
        static void Login()
        {
            Console.Clear();
            Console.WriteLine("====== LOGIN ======");

            string phone = GetPhone("  Mobile Number: ");

            if (!gcash.AccountExists(phone))
            { 
                Console.WriteLine("Account not found.");
                Console.ReadKey();
                return; 
            }

            int attempts = 0;
            while (attempts < 3)
            {
                string mpin = GetMpin("  MPIN: ");
                string result = gcash.Login(phone, mpin);

                switch (result)
                {
                    case "OK":
                        Console.Clear();
                        Console.WriteLine($"Welcome back, {gcash.CurrentUser.Name}!");
                        Console.ReadKey();
                        Dashboard();
                        return;

                    case "LOCKED":
                        Console.WriteLine("Account is LOCKED. Contact GCash Support (2882).");
                        Console.ReadKey();
                        return;

                    case "LOCKED_NOW":
                        Console.WriteLine("Too many failed attempts. Account is now LOCKED!");
                        Console.WriteLine("Contact GCash Support at 2882 to unlock.");
                        Console.ReadKey();
                        return;

                    default:
                        attempts++;
                        int left = 3 - attempts;
                        if (left > 0)
                            Console.Write($"Incorrect MPIN. {left} attempt(s) left.");
                        break;
                }
            }
        }

        /* =========================================================
                               DASHBOARD
         =========================================================*/
        static void Dashboard()
        {
            while (gcash.CurrentUser != null)
            {
                Console.Clear();
                Console.WriteLine($"GCash — {gcash.CurrentUser.Name}");
                Console.WriteLine("==========================================");
                Console.WriteLine($"  Balance: P {gcash.CurrentUser.GetBalance()}   [{gcash.CurrentUser.AccountType}]");
                Console.WriteLine("==========================================");
                Console.WriteLine("  MONEY");
                Console.WriteLine("==========================================");
                Console.WriteLine("   [1]  Cash In");
                Console.WriteLine("   [2]  Cash Out");
                Console.WriteLine("   [3]  Send Money (GCash-to-GCash)");
                Console.WriteLine("   [4]  Bank Transfer");
                Console.WriteLine("==========================================");
                Console.WriteLine("  LOAD");
                Console.WriteLine("==========================================");
                Console.WriteLine("   [5]  Buy Load");
                Console.WriteLine("==========================================");
                Console.WriteLine("  BORROW");
                Console.WriteLine("==========================================");
                Console.WriteLine("  [6]  GCredit");
                Console.WriteLine("==========================================");
                Console.WriteLine("  ACCOUNT");
                Console.WriteLine("==========================================");
                Console.WriteLine("  [7]  My Wallet & Portfolio");
                Console.WriteLine("  [8]  Transaction History");
                Console.WriteLine("  [9]  Account Benefits / Limits");
                Console.WriteLine("  [10]  Change MPIN");
                Console.WriteLine("  [11]  Logout");
                Console.WriteLine("==========================================");
                Console.Write("  Select: ");

                switch (Console.ReadLine()?.Trim())
                {
                    case "1": DoCashIn(); break;
                    case "2": DoCashOut(); break;
                    case "3": DoSendMoney(); break;
                    case "4": DoBankTransfer(); break;
                    case "5": DoBuyLoad(); break;
                    case "6": DoGCredit(); break;
                    case "7": DoWallet(); break;
                    case "8": DoHistory(); break;
                    case "9": DoBenefits(); break;
                    case "10": DoChangeMpin(); break;
                    case "11": DoLogout(); return;
                    default: 
                        Console.WriteLine("\nError: Invalid option.");
                        Console.ReadKey();
                        break;
                }
            }
        }

        /* =========================================================
                             FEATURE HANDLERS
        =========================================================*/

        static void DoCashIn()
        {
            Console.Clear();
            Console.WriteLine("====== CASH IN ======");
            Console.WriteLine("  Sources: GCash Partner Outlets, OTC, Online Banking");
            Console.WriteLine($"  Daily Limit: P {gcash.CurrentUser.GetCashInLimit()}");
            try
            {
                decimal amt = GetAmount("  Amount to Cash In: P");
                gcash.CashIn(amt);
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex.Message);
            }
            Console.ReadKey();
        }

        static void DoCashOut()
        {
            Console.Clear();
            Console.WriteLine("====== CASH OUT ======");
            Console.WriteLine("  Outlets: GCash Partner Stores, Pawnshops, Banks");
            Console.WriteLine($"  Daily Limit: P {gcash.CurrentUser.GetCashOutLimit()}");
            try
            {
                decimal amt = GetAmount("  Amount to Cash Out: P");
                gcash.CashOut(amt);
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex.Message); 
            }
            Console.ReadKey();
        }

        static void DoSendMoney()
        {
            Console.Clear();
            Console.WriteLine("====== SEND MONEY ======");
            Console.WriteLine($"  Free GCash-to-GCash transfers!");
            Console.WriteLine($"  Daily Limit: P {gcash.CurrentUser.SendLimit}");
            try
            {
                string to = GetPhone("  Recipient GCash Number: ");
                decimal amt = GetAmount("  Amount: P");
                gcash.SendMoney(to, amt);
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex.Message); 
            }
            Console.ReadKey();
        }

        static void DoBankTransfer()
        {
            Console.Clear();
            Console.WriteLine("====== BANK TRANSFER ======");
            Console.WriteLine("  Supported: BPI, BDO, Metrobank, UnionBank, Landbank, +more");
            try
            {
                Console.Write("  Bank Name   : "); string bank = Console.ReadLine().Trim();
                Console.Write("  Account No  : "); string accNo = Console.ReadLine().Trim();
                decimal amt = GetAmount("  Amount: P");
                gcash.BankTransfer(bank, accNo, amt);
            }
            catch (Exception ex) 
            { 
                Console.WriteLine(ex.Message);
            }
            Console.ReadKey();
        }

        static void DoBuyLoad()
        {
            Console.Clear();
            Console.WriteLine("====== BUY LOAD ======");
            Console.WriteLine("  Available Promos:");
            foreach (var p in gcash.GetLoadPromos())
                Console.WriteLine($"   [{p.Key}] {p.Value.desc,-32}  P{p.Value.price:N2}");
            try
            {
                string targetPhone = GetPhone("  Mobile Number to Load: ");
                string key = GetChoice("  Select Promo: ", gcash.GetLoadPromos().Keys);
                gcash.BuyLoad(targetPhone, key);
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex.Message); 
            }
            Console.ReadKey();
        }

        static void DoGCredit()
        {
            Console.Clear();
            Console.WriteLine("====== GCREDIT ======");
            decimal avail = gcash.CurrentUser.CreditLine - gcash.CurrentUser.CreditUsed;
            Console.WriteLine($"  Credit Line    : P {gcash.CurrentUser.CreditLine}");
            Console.WriteLine($"  Used           : P {gcash.CurrentUser.CreditUsed}");
            Console.WriteLine($"  Available      : P {avail}");
            Console.WriteLine("  [Verified accounts only]");
            Console.WriteLine("  [1] Use GCredit");
            Console.WriteLine("  [2] Pay GCredit");
            Console.Write("  Select: ");
            try
            {
                switch (Console.ReadLine()?.Trim())
                {
                    case "1":
                        Console.Write("  Purpose: "); string purpose = Console.ReadLine().Trim();
                        decimal use = GetAmount("  Amount: P");
                        gcash.UseGCredit(purpose, use); break;
                    case "2":
                        decimal pay = GetAmount("  Payment Amount: P");
                        gcash.PayGCredit(pay); break;
                    default:
                        Console.WriteLine("\nError: Invalid."); 
                        break;
                }
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex.Message);
            }
            Console.ReadKey();
        }


        static void DoWallet()
        {
            Console.Clear();
            Console.WriteLine("MY WALLET & PORTFOLIO");
            gcash.CurrentUser.ShowWallet();
            Console.ReadKey();
        }

        static void DoHistory()
        {
            Console.Clear();
            Console.WriteLine("TRANSACTION HISTORY  (last 20)");
            gcash.CurrentUser.ShowHistory();
            Console.ReadKey();
        }

        static void DoBenefits()
        {
            Console.Clear();
            Console.WriteLine("ACCOUNT BENEFITS & LIMITS");
            Console.WriteLine(gcash.CurrentUser.GetAccountBenefits());
            Console.ReadKey();
        }

        static void DoChangeMpin()
        {
            Console.Clear(); 
            Console.WriteLine("====== CHANGE MPIN ======");
            string current = GetMpin("  Current MPIN: ");
            if (!gcash.CurrentUser.ValidatePin(current))
            {
                Console.WriteLine("Incorrect MPIN.");
                Console.ReadKey();
                return; }
            string newPin = GetMpin("  New MPIN    : ");
            string confirm = GetMpin("  Confirm MPIN: ");

            if (newPin != confirm)
            { 
                Console.WriteLine("MPINs do not match.");
                Console.ReadKey();
                return; }
                gcash.ChangeMpin(newPin);
                Console.WriteLine("\n MPIN changed successfully!");
                Console.ReadKey();
        }

        static void DoLogout()
        {
            string name = gcash.CurrentUser.Name;
            gcash.Logout();
            Console.WriteLine($"Logged out. Ingat, {name}!");
            Console.ReadKey();
        }

        /* =========================================================
                             INPUT HELPERS
         =========================================================*/
        static string GetNonEmpty(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string v = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
                Console.Write("\nError: Field cannot be empty.");
            }
        }

        static string GetPhone(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string v = Console.ReadLine()?.Trim();
                if (Regex.IsMatch(v ?? "", @"^(09|\+639)\d{9}$")) return v;
                Console.Write("\nError: Enter a valid PH mobile number (e.g. 09XXXXXXXXX).");
            }
        }

        static string GetEmail(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string v = Console.ReadLine()?.Trim();
                if (Regex.IsMatch(v ?? "", @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return v;
                Console.Write("\nError: Invalid email format.");
            }
        }

        static string GetMpin(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string v = Console.ReadLine()?.Trim();
                if (Regex.IsMatch(v ?? "", @"^\d{4}$")) return v;
                Console.Write("\nError: MPIN must be exactly 4 digits.");
            }
        }

        static decimal GetAmount(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim();
                if (decimal.TryParse(input, out decimal v) && v > 0) return v;
                Console.Write("\nError: Enter a valid positive amount.");
            }
        }

        static string GetChoice(string prompt, IEnumerable<string> valid)
        {
            var validSet = new HashSet<string>(valid);
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim();
                if (validSet.Contains(input)) return input;
                Console.Write("\nError: Invalid choice.");
            }
        }
    }
}