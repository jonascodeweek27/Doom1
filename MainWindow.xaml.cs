using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.IO;

namespace DoomWpf
{
    public partial class MainWindow : Window

    {
        int currentFloor = 1;

        void LoadLevel(int level)
        {
            currentLevel = level;
            currentFloor = 1;
            changingLevel = false;

            // Die schwere Sturmkanone wird beim Erreichen von Level 13 freigeschaltet.
            if (level >= 13 && !heavyWeaponUnlocked)
            {
                heavyWeaponUnlocked = true;
                heavyAmmo = HeavyWeaponAmmoCapacity;
                selectedWeapon = 2;
            }

            powerUpAvailable = false;
            powerUpTicks = 0;
            secondSupplyAvailable = false;
            traps.Clear();
            enemyProjectiles.Clear();

            // Level 3 behält seine zwei Ebenen.
            if (level == 3)
            {
                LoadLevelFloor(1);
                return;
            }

            bool bossLevel = level == 5 || level == 10 || level == 15 || level == 20;

            if (bossLevel && level != 20)
            {
                // Mini-Bosse (Level 5, 10, 15) haben ihren eigenen Obsidian-Bossraum.
                map = MiniBossArenaMap();
            }
            else if (bossLevel)
            {
                // Der Bossraum liegt am rechten Ende der Karte. Die roten
                // Wandsegmente ('3') bilden eine eigene Arena mit einem Eingang
                // in der Mitte. Der Boss wartet innerhalb der Arena.
                map = FinalBossArenaMap();
            }
            else
            {
                map = NormalMap();
            }

            px = 3.5;
            py = 3.5;

            // Ab Level 5 steigen Leben, Tempo und Schaden deutlich.
            int difficulty = Math.Max(0, level - 4);
            int normalHp = 3 + difficulty / 2;
            double normalSpeed = Math.Min(0.045, 0.025 + difficulty * 0.0015);
            double normalDamage = Math.Min(0.65, 0.25 + difficulty * 0.025);

            // Globaler Schwierigkeitsgrad: wirkt zusätzlich zur Level-Skalierung.
            double hpScale = difficultyLevel switch { 0 => 0.70, 1 => 1.0, 2 => 1.35, _ => 1.75 };
            double speedScale = difficultyLevel switch { 0 => 0.82, 1 => 1.0, 2 => 1.12, _ => 1.25 };
            double damageScale = difficultyLevel switch { 0 => 0.55, 1 => 1.0, 2 => 1.35, _ => 1.75 };
            normalHp = Math.Max(1, (int)Math.Round(normalHp * hpScale));
            normalSpeed *= speedScale;
            normalDamage *= damageScale;

            enemies = new List<Enemy>();
            // Größere Karten: mehr Spawnpunkte, passend zur jeweiligen Kartenform.
            double[,] normalSpawns =
            {
                { 10.5, 3.5 }, { 16.5, 1.5 }, { 17.5, 6.5 }, { 5.5, 8.5 },
                { 13.5, 9.5 }, { 2.5, 13.5 }, { 17.5, 12.5 }, { 9.5, 11.5 },
                { 14.5, 4.5 }, { 7.5, 14.5 }, { 11.5, 6.5 }, { 3.5, 10.5 }
            };
            double[,] bossSpawns =
            {
                { 8.5, 1.5 }, { 11.5, 3.5 }, { 9.5, 6.5 }, { 5.5, 10.5 },
                { 16.5, 2.5 }, { 20.5, 2.5 }, { 16.5, 10.5 }, { 20.5, 10.5 },
                { 18.5, 8.5 }, { 15.5, 6.5 }
            };
            double[,] spawns = bossLevel ? bossSpawns : normalSpawns;

            int count = Math.Min(3 + level / 2, spawns.GetLength(0));
            for (int i = 0; i < count; i++)
            {
                EnemyKind kind = EnemyKind.Imp;
                int hp = normalHp;
                double damage = normalDamage;
                double speed = normalSpeed;

                // Ab Level 10 erscheinen zwei neue Gegnertypen:
                // schnelle Schattenjäger und gepanzerte Brutes.
                if (level >= 10)
                {
                    if (i % 3 == 0)
                    {
                        kind = EnemyKind.Stalker;
                        hp = normalHp + 1;
                        speed = Math.Min(0.065, normalSpeed * 1.55);
                    }
                    else if (i % 3 == 1)
                    {
                        kind = EnemyKind.Brute;
                        hp = normalHp + 4;
                        damage = Math.Min(0.85, normalDamage * 1.30);
                        speed = normalSpeed * 0.76;
                    }
                }

                enemies.Add(new Enemy(
                    spawns[i, 0], spawns[i, 1],
                    hp: hp,
                    damage: damage,
                    speed: speed,
                    kind: kind));
            }

            // Mini-Bosse in Level 5, 10 und 15; Endboss in Level 20.
            if (level == 5 || level == 10 || level == 15)
            {
                enemies.Add(new Enemy(18.5, 6.5,
                    hp: (int)Math.Round((18 + level * 2) * hpScale), isBoss: true,
                    damage: (0.42 + level * 0.012) * damageScale,
                    speed: (0.022 + level * 0.0005) * speedScale));
            }
            else if (level == 20)
            {
                // Der Endboss hat deutlich mehr Leben und ist schneller.
                enemies.Add(new Enemy(18.5, 6.5,
                    hp: (int)Math.Round(120 * hpScale), isBoss: true,
                    damage: 0.85 * damageScale, speed: 0.035 * speedScale));
            }

            FixEnemySpawns();
            SpawnSupplyBox();
            SpawnPowerUp(); // Power-ups werden erst ab Level 8 erzeugt.
            SpawnTraps();
        }

        // Eigener Bossraum der Mini-Bosse: abgeschlossene Obsidian-Kammer (rote Wände '3')
        // mit einem Eingang von links und vier Säulen. Der Boss wartet bei (11.5, 5.5).
        static string[] MiniBossArenaMap()
        {
            // 24 x 14 Felder: Vorraum links, Arena rechts mit breitem Eingang (Zeilen 5-8).
            return new string[]
            {
                "111111111111111111111111",
                "100000000000033333333331",
                "101100000000030000000001",
                "100000000000030000000001",
                "100000110000030030003001",
                "100000000000000000000001",
                "100000000000000000000001",
                "100000000000000000000001",
                "100000000000000000000001",
                "100000110000030030003001",
                "100000000000030000000001",
                "101100000000030000000001",
                "100000000000033333333331",
                "111111111111111111111111"
            };
        }

        // Endboss-Arena: wie der Mini-Boss-Raum, aber mit zusätzlichen Säulen.
        static string[] FinalBossArenaMap()
        {
            var m = MiniBossArenaMap();
            m[3] = "1000000000000" + "3" + "000303030" + "1";
            m[10] = "1000000000000" + "3" + "000303030" + "1";
            return m;
        }

        // Normale Level-Karte: 20 x 16 Felder.
        static string[] NormalMap()
        {
            return new string[]
            {
                "11111111111111111111",
                "10000000000000000001",
                "10110001000110001001",
                "10000001000000001001",
                "10001000001000000001",
                "10001011000101100001",
                "10000000000000000001",
                "10110001000110001001",
                "10000000100000001001",
                "10001000000100000001",
                "10000010001000101001",
                "10110000000000100001",
                "10000001000110000001",
                "10001000000000010001",
                "10000000000000000001",
                "11111111111111111111"
            };
        }

        // Lädt eine der beiden Ebenen von Level 3.
        void LoadLevelFloor(int floor)
        {
            currentLevel = 3;
            currentFloor = floor;
            changingLevel = false;
            powerUpAvailable = false;
            powerUpTicks = 0;
            secondSupplyAvailable = false;
            traps.Clear();
            enemyProjectiles.Clear();

            if (floor == 1)
            {
                var floor1 = NormalMap();
                floor1[13] = floor1[13].Remove(17, 1).Insert(17, "2"); // Aufgang
                map = floor1;

                enemies = new List<Enemy>
                {
                    new Enemy(10.5, 3.5),
                    new Enemy(5.5, 8.5),
                    new Enemy(13.5, 9.5),
                    new Enemy(16.5, 1.5)
                };
            }
            else
            {
                var floor2 = MiniBossArenaMap();
                floor2[10] = floor2[10].Remove(2, 1).Insert(2, "2"); // Aufgang außerhalb des Bossraums
                map = floor2;

                enemies = new List<Enemy>
                {
                    new Enemy(5.5, 3.5),
                    new Enemy(9.5, 2.5),
                    new Enemy(8.5, 8.5),
                    // Mini-Boss: 18 Leben, mehr Schaden und größerer Sprite.
                    new Enemy(18.5, 6.5,
                              hp: Math.Max(1, (int)Math.Round(18 * (difficultyLevel switch { 0 => 0.70, 1 => 1.0, 2 => 1.35, _ => 1.75 }))), isBoss: true,
                              damage: 0.5 * (difficultyLevel switch { 0 => 0.55, 1 => 1.0, 2 => 1.35, _ => 1.75 }),
                              speed: 0.022 * (difficultyLevel switch { 0 => 0.82, 1 => 1.0, 2 => 1.12, _ => 1.25 }))
                };
            }

            px = 3.5;
            py = 3.5;
            FixEnemySpawns();
            SpawnSupplyBox();
            SpawnTraps();
        }

        int currentLevel = 1;
        bool changingLevel = false;


        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT point);
        void MoveEnemies()
        {
            if (trainingMode) return;
            foreach (var enemy in enemies.Where(x => x.Alive))
            {
                double detectionRange = enemy.IsBoss ? 4.5 : 3.0;
                double stopRange = enemy.IsBoss ? 0.85 : 0.6;

                double dx = px - enemy.X;
                double dy = py - enemy.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance > detectionRange || distance <= stopRange)
                    continue;

                double moveX = dx / distance * enemy.Speed;
                double moveY = dy / distance * enemy.Speed;

                // Jeder Bewegungsanteil wird separat auf Wände geprüft.
                if (!IsWall(enemy.X + moveX, enemy.Y))
                    enemy.X += moveX;

                if (!IsWall(enemy.X, enemy.Y + moveY))
                    enemy.Y += moveY;
            }
        }

        void UpdateBossRangedAttacks()
        {
            if (trainingMode) return;
            foreach (var enemy in enemies.Where(e => e.Alive && e.IsBoss))
            {
                if (enemy.RangedCooldownTicks > 0)
                {
                    enemy.RangedCooldownTicks--;
                    continue;
                }

                double dx = px - enemy.X;
                double dy = py - enemy.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 1.0 || distance > 7.5 || !ClearPath(enemy.X, enemy.Y, px, py))
                    continue;

                double speed = currentLevel >= 20 ? 0.13 : 0.105;
                double vx = dx / distance * speed;
                double vy = dy / distance * speed;
                bool finalBoss = currentLevel >= 20;
                double projectileDamage = finalBoss ? 9.5 : 7.5 + currentLevel * 0.12;
                enemyProjectiles.Add(new EnemyProjectile(
                    enemy.X + dx / distance * 0.30,
                    enemy.Y + dy / distance * 0.30,
                    vx, vy, projectileDamage, finalBoss));

                // Der Endboss schießt deutlich öfter als die Mini-Bosse.
                enemy.RangedCooldownTicks = finalBoss ? 42 : Math.Max(52, 92 - currentLevel * 2);
            }
        }

        void UpdateEnemyProjectiles()
        {
            for (int i = enemyProjectiles.Count - 1; i >= 0; i--)
            {
                var shot = enemyProjectiles[i];
                shot.X += shot.VX;
                shot.Y += shot.VY;
                shot.LifeTicks--;

                if (shot.LifeTicks <= 0 || IsWall(shot.X, shot.Y))
                {
                    enemyProjectiles.RemoveAt(i);
                    continue;
                }

                if (Distance(px, py, shot.X, shot.Y) <= 0.34)
                {
                    // Durch einen ausreichend hohen Sprung kann die Feuerkugel
                    // über den Spieler hinwegfliegen.
                    if (jumpHeight < 10.0 && dashTicks <= 0)
                    {
                        TakeDamage(shot.Damage);
                        trapMessage = shot.IsFinalBoss
                            ? "END-BOSS: FEUERKUGEL GETROFFEN!"
                            : "MINI-BOSS: FEUERKUGEL GETROFFEN!";
                        trapMessageTicks = 75;
                    }
                    else
                    {
                        trapMessage = dashTicks > 0 ? "FEUERKUGEL AUSGEWICHEN!" : "FEUERKUGEL ÜBERSPRUNGEN!";
                        trapMessageTicks = 45;
                    }
                    enemyProjectiles.RemoveAt(i);

                    if (health <= 0)
                    {
                        health = 0;
                        dead = true;
                        EndGame("GAME OVER – BOSSANGRIFF");
                        return;
                    }
                }
            }
        }

        void DrawEnemyProjectiles()
        {
            int horizon = GetViewHorizon();
            foreach (var shot in enemyProjectiles.OrderByDescending(p => Distance(px, py, p.X, p.Y)))
            {
                double dx = shot.X - px;
                double dy = shot.Y - py;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 0.15) continue;

                double direction = Math.Atan2(dy, dx);
                double difference = Math.Atan2(Math.Sin(direction - angle), Math.Cos(direction - angle));
                if (Math.Abs(difference) > 0.9) continue;

                int screenX = (int)(W / 2.0 + Math.Tan(difference) * W / 1.32);
                int size = Math.Clamp((int)(H / distance * (shot.IsFinalBoss ? 0.16 : 0.14)), 3, 27);
                int screenY = horizon - size / 3;
                int radius = Math.Max(2, size / 2);

                Color outer = shot.IsFinalBoss
                    ? Color.FromRgb(255, 54, 18)
                    : Color.FromRgb(155, 45, 240);
                Color core = shot.IsFinalBoss
                    ? Color.FromRgb(255, 220, 76)
                    : Color.FromRgb(210, 170, 255);

                for (int y = screenY - radius - 3; y <= screenY + radius + 3; y++)
                {
                    for (int x = screenX - radius - 3; x <= screenX + radius + 3; x++)
                    {
                        if (x < 0 || x >= W || y < 0 || y >= H || distance >= depth[x]) continue;
                        double d = Math.Sqrt((x - screenX) * (x - screenX) + (y - screenY) * (y - screenY));
                        if (d > radius + 3) continue;
                        Color c = d <= radius * 0.42 ? Colors.White : d <= radius ? core : outer;
                        SetPixel(x, y, c);
                    }
                }
            }
        }

        void CenterMouse()
        {
            Point center = GameImage.PointToScreen(
                new Point(GameImage.ActualWidth / 2,
                          GameImage.ActualHeight / 2));

            SetCursorPos((int)center.X, (int)center.Y);
        }
        const int W = 400;
        const int H = 250;

        WriteableBitmap bitmap;
        byte[] pixels = new byte[W * H * 4];
        double[] depth = new double[W];

        DispatcherTimer timer;
        HashSet<Key> keys = new();

        double px = 3.5, py = 3.5;
        double angle = 0;
        double health = 100;
        double armor = 82; // Rüstungsanzeige wie im klassischen Doom-HUD.
        int kills = 0;
        const int PistolAmmoCapacity = 75; // Waffe 1
        int ammo = PistolAmmoCapacity; // Pistolenmunition
        const int HeavyWeaponAmmoCapacity = 50; // Waffe 2
        const int HeavyWeaponDamage = 4;
        int heavyAmmo = 0;
        int selectedWeapon = 1; // 1 = Pistole, 2 = schwere Sturmkanone
        bool heavyWeaponUnlocked = false;
        List<BulletEffect> bulletEffects = new();
        readonly List<EnemyProjectile> enemyProjectiles = new();

        // Sprungphysik (Leertaste) und Kamerahöhe für den Raycaster.
        double jumpHeight = 0;
        double jumpVelocity = 0;
        const double JumpGravity = 0.42;
        const double JumpImpulse = 4.6;

        // Bewegung: Der Spieler geht langsam und muss für Tempo sprinten (Shift)
        // oder per Dash (Q) ausweichen. Beides kostet Ausdauer.
        const double WalkSpeed = 0.045;
        const double SprintSpeed = 0.095;
        const double DashSpeed = 0.23;
        const int DashDurationTicks = 10;
        const int DashCooldownTicks = 70;
        const double DashStaminaCost = 30;
        const double StaminaMax = 100;
        double stamina = StaminaMax;
        bool staminaExhausted = false;
        int staminaRegenDelay = 0;
        bool sprinting = false;
        int dashTicks = 0;
        int dashCooldown = 0;
        double dashDirX = 0, dashDirY = 0;

        // Nahkampfwaffe: wird automatisch benutzt, wenn die gewählte Waffe keine Munition hat.
        const int MeleeSwingDuration = 14;
        const int MeleeCooldownTicks = 26;
        const double MeleeRange = 1.25;
        int meleeCooldown = 0;
        int meleeSwingTicks = 0;

        // Boden- und Wandfallen werden auf geeigneten freien Feldern platziert.
        readonly List<Trap> traps = new();
        string trapMessage = "";
        int trapMessageTicks = 0;
        static readonly Random random = new Random();

        // Ab Level 8 kann gelegentlich eine zweite Versorgungskiste erscheinen.
        double secondSupplyX = 0;
        double secondSupplyY = 0;
        bool secondSupplyAvailable = false;

        // Laufzeit-Menü: Level-Auswahl funktioniert sowohl im Hauptmenü als auch in Pause.
        ComboBox levelSelector;
        ComboBox difficultySelector;
        ComboBox modeSelector;
        bool trainingMode = false;
        int difficultyLevel = 1; // 0=Leicht, 1=Normal, 2=Schwer, 3=Albtraum
        Button loadSelectedLevelButton;
        Button resumeButton;
        Button mainMenuStartButton;
        bool isPaused = false;

        // Pro Level bzw. Level-3-Ebene gibt es eine Versorgungskiste.
        double supplyX = 0;
        double supplyY = 0;
        bool supplyAvailable = false;
        string supplyMessage = "";
        int supplyMessageTicks = 0;

        // Power-up: sammelt man es ein, verursacht die Pistole 30 Sekunden lang
        // doppelten Schaden. Die Anzeige läuft über den normalen Spieltimer.
        double powerUpX = 0;
        double powerUpY = 0;
        bool powerUpAvailable = false;
        int powerUpTicks = 0;
        string powerUpMessage = "";
        int powerUpMessageTicks = 0;

        // Der Soundtrack wird nur von einer lokalen Datei abgespielt, wenn sie
        // der Nutzer selbst in Assets/DoomSoundtrack.mp3 bereitstellt.
        MediaPlayer soundtrackPlayer;
        bool soundtrackAvailable = false;

        bool started = false;
        bool dead = false;
        bool won = false;

        Point lastMouse;
        bool mouseInitialized = false;

        string[] map =
        {
            "111111111111",
            "100000000001",
            "101100010001",
            "100000010001",
            "100010000001",
            "100010110001",
            "100000000001",
            "101100010001",
            "100000000001",
            "111111111111"
        };



        enum EnemyKind
        {
            Imp,
            Stalker,
            Brute
        }

        class Enemy
        {
            public double X, Y;
            public int HP;
            public int MaxHP;
            public bool Alive = true;
            public bool IsBoss;
            public double Damage;
            public double Speed;
            public EnemyKind Kind;
            public int RangedCooldownTicks;
            public int HitFlashTicks;
            public double Phase = random.NextDouble() * Math.PI * 2;

            public Enemy(
                double x,
                double y,
                int hp = 3,
                bool isBoss = false,
                double damage = 0.25,
                double speed = 0.025,
                EnemyKind kind = EnemyKind.Imp)
            {
                X = x;
                Y = y;
                HP = hp;
                MaxHP = hp;
                IsBoss = isBoss;
                Damage = damage;
                Speed = speed;
                Kind = kind;
                RangedCooldownTicks = isBoss ? 55 : 0;
            }
        }

        // Rein visueller Projektil-Effekt: Die Schadensberechnung bleibt in Shoot().
        class BulletEffect
        {
            public double Progress;
            public bool HeavyWeapon;

            public BulletEffect(double progress = 0, bool heavyWeapon = false)
            {
                Progress = progress;
                HeavyWeapon = heavyWeapon;
            }
        }

        enum TrapKind
        {
            FloorSpikes,
            LavaCrack
        }

        class Trap
        {
            public double X;
            public double Y;
            public TrapKind Kind;
            public int CooldownTicks;
            public int ActiveTicks;

            public Trap(double x, double y, TrapKind kind)
            {
                X = x;
                Y = y;
                Kind = kind;
            }
        }

        // Magische Feuerkugeln der Mini-Bosse und des Endbosses.
        class EnemyProjectile
        {
            public double X, Y;
            public double VX, VY;
            public double Damage;
            public bool IsFinalBoss;
            public int LifeTicks = 100;

            public EnemyProjectile(double x, double y, double vx, double vy,
                                   double damage, bool isFinalBoss)
            {
                X = x; Y = y; VX = vx; VY = vy;
                Damage = damage;
                IsFinalBoss = isFinalBoss;
            }
        }


        List<Enemy> enemies = new()
        {
            new Enemy(8.5, 2.5),
            new Enemy(5.5, 4.5),
            new Enemy(9.5, 7.5)
        };

        public MainWindow()
        {
            InitializeComponent();

            // Die alten Textanzeigen oben links werden durch das Doom-HUD unten ersetzt.
            HealthText.Visibility = Visibility.Collapsed;
            EnemyText.Visibility = Visibility.Collapsed;
            InitializeLevelMenu();

            bitmap = new WriteableBitmap(
                W, H, 96, 96,
                PixelFormats.Bgra32, null);

            InitializeTextures();
            GameImage.Source = bitmap;

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            timer.Tick += GameLoop;
            InitializeSoundtrack();
        }

        void InitializeSoundtrack()
        {
            soundtrackPlayer = new MediaPlayer
            {
                Volume = 0.35
            };

            // Schleife, solange das Spiel läuft. Die Audiodatei wird nicht
            // mit dem Projekt ausgeliefert und muss selbst hinzugefügt werden.
            soundtrackPlayer.MediaEnded += (sender, args) =>
            {
                soundtrackPlayer.Position = TimeSpan.Zero;
                if (started && !dead && !won)
                    soundtrackPlayer.Play();
            };

            soundtrackPlayer.MediaFailed += (sender, args) =>
            {
                soundtrackAvailable = false;
            };

            string soundtrackPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "DoomSoundtrack.mp3");

            if (File.Exists(soundtrackPath))
            {
                soundtrackPlayer.Open(new Uri(soundtrackPath, UriKind.Absolute));
                soundtrackAvailable = true;
            }
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Focus();
        }

        void InitializeLevelMenu()
        {
            var panel = Overlay.Child as StackPanel;
            if (panel == null)
                return;

            // Der ursprüngliche Startknopf bleibt erhalten und nutzt die Auswahl.
            mainMenuStartButton = panel.Children.OfType<Button>().FirstOrDefault();

            var levelLabel = new TextBlock
            {
                Text = "LEVEL AUSWÄHLEN",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 3)
            };
            panel.Children.Add(levelLabel);

            levelSelector = new ComboBox
            {
                Width = 190,
                Height = 27,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
                ToolTip = "Level auswählen"
            };
            for (int level = 1; level <= 20; level++)
                levelSelector.Items.Add(level == 20 ? "Level 20 – Endboss" : $"Level {level:00}");
            levelSelector.SelectedIndex = 0;
            panel.Children.Add(levelSelector);

            loadSelectedLevelButton = new Button
            {
                Content = "LEVEL STARTEN / LADEN",
                Width = 190,
                Height = 27,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(5, 2, 5, 2)
            };
            loadSelectedLevelButton.Click += (sender, args) => StartSelectedLevel();
            panel.Children.Add(loadSelectedLevelButton);

            resumeButton = new Button
            {
                Content = "FORTSETZEN",
                Width = 190,
                Height = 27,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(5, 2, 5, 2),
                Visibility = Visibility.Collapsed
            };
            resumeButton.Click += (sender, args) => ResumeGameplay();
            panel.Children.Add(resumeButton);

            AddMenuLabel(panel, "SCHWIERIGKEIT");
            difficultySelector = new ComboBox
            {
                Width = 190,
                Height = 27,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            };
            difficultySelector.Items.Add("Leicht");
            difficultySelector.Items.Add("Normal");
            difficultySelector.Items.Add("Schwer");
            difficultySelector.Items.Add("Albtraum");
            difficultySelector.SelectedIndex = 1;
            panel.Children.Add(difficultySelector);

            AddMenuLabel(panel, "SPIELMODUS");
            modeSelector = new ComboBox
            {
                Width = 190,
                Height = 27,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 7)
            };
            modeSelector.Items.Add("Kampagne");
            modeSelector.Items.Add("Trainingsmodus");
            modeSelector.SelectedIndex = 0;
            panel.Children.Add(modeSelector);

            var tutorialButton = new Button
            {
                Content = "TUTORIAL / STEUERUNG",
                Width = 190,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            tutorialButton.Click += (sender, args) => ShowTutorial();
            panel.Children.Add(tutorialButton);
        }

        void AddMenuLabel(StackPanel panel, string caption)
        {
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 2)
            });
        }

        void ShowTutorial()
        {
            MessageBox.Show(
                "DOOM WPF – TUTORIAL\\n\\n" +
                "WASD: bewegen und seitwärts laufen\\n" +
                "Maus: umsehen und zielen\\n" +
                "Linksklick: schießen\\n" +
                "Leertaste: springen – kann Angriffe überbrücken\\n" +
                "Shift: sprinten (verbraucht Ausdauer)\\n" +
                "Q: Dash in Laufrichtung (kostet Ausdauer)\\n" +
                "1 / 2: Waffe wechseln (Sturmkanone ab Level 13)\\n" +
                "Esc: pausieren\\n" +
                "Tipp: Versorgungskisten und Power-ups einsammeln. Bei Boss-Feuerkugeln springen oder dashen.",
                "Tutorial", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void Start_Click(object sender, RoutedEventArgs e)
        {
            if (isPaused && !dead && !won)
            {
                ResumeGameplay();
                return;
            }

            StartSelectedLevel();
        }

        void StartSelectedLevel()
        {
            int selectedLevel = levelSelector != null && levelSelector.SelectedIndex >= 0
                ? levelSelector.SelectedIndex + 1
                : 1;

            difficultyLevel = difficultySelector != null && difficultySelector.SelectedIndex >= 0
                ? difficultySelector.SelectedIndex : 1;
            trainingMode = modeSelector != null && modeSelector.SelectedIndex == 1;
            Restart(selectedLevel);
            isPaused = false;
            if (resumeButton != null)
                resumeButton.Visibility = Visibility.Collapsed;
            if (mainMenuStartButton != null)
                mainMenuStartButton.Content = "SPIEL STARTEN";

            started = true;
            Overlay.Visibility = Visibility.Collapsed;
            Cursor = Cursors.None;
            CenterMouse();
            timer.Start();
            if (soundtrackAvailable)
            {
                soundtrackPlayer.Position = TimeSpan.Zero;
                soundtrackPlayer.Play();
            }
            Focus();
        }

        void ResumeGameplay()
        {
            if (dead || won)
                return;

            isPaused = false;
            started = true;
            Overlay.Visibility = Visibility.Collapsed;
            if (resumeButton != null)
                resumeButton.Visibility = Visibility.Collapsed;
            if (mainMenuStartButton != null)
                mainMenuStartButton.Content = "SPIEL STARTEN";
            Cursor = Cursors.None;
            CenterMouse();
            timer.Start();
            if (soundtrackAvailable)
                soundtrackPlayer.Play();
            Focus();
        }

        void SetMenuTitle(string title)
        {
            var panel = Overlay.Child as StackPanel;
            if (panel != null && panel.Children.Count > 0 && panel.Children[0] is TextBlock heading)
                heading.Text = title;
        }

        void Restart(int level = 1)
        {
            health = 100;
            armor = 82;
            kills = 0;
            ammo = PistolAmmoCapacity;
            heavyWeaponUnlocked = level >= 13;
            heavyAmmo = heavyWeaponUnlocked ? HeavyWeaponAmmoCapacity : 0;
            selectedWeapon = heavyWeaponUnlocked ? 2 : 1;
            dead = false;
            won = false;
            started = false;
            isPaused = false;
            currentLevel = level;
            currentFloor = 1;
            changingLevel = false;
            keys.Clear();
            bulletEffects.Clear();
            enemyProjectiles.Clear();
            mouseInitialized = false;
            stamina = StaminaMax;
            staminaExhausted = false;
            staminaRegenDelay = 0;
            sprinting = false;
            dashTicks = 0;
            dashCooldown = 0;
            meleeCooldown = 0;
            meleeSwingTicks = 0;
            jumpHeight = 0;
            jumpVelocity = 0;
            powerUpAvailable = false;
            powerUpTicks = 0;
            powerUpMessageTicks = 0;
            trapMessage = "";
            trapMessageTicks = 0;
            traps.Clear();
            secondSupplyAvailable = false;

            if (soundtrackAvailable)
                soundtrackPlayer.Position = TimeSpan.Zero;

            LoadLevel(level);
            Render();
        }

        void Window_KeyDown(object sender, KeyEventArgs e)
        {
            bool keyWasAlreadyDown = keys.Contains(e.Key);
            keys.Add(e.Key);

            // Waffenwechsel mit 1 und 2 (Zahlenreihe oder Nummernblock).
            if (!keyWasAlreadyDown && started &&
                (e.Key == Key.D1 || e.Key == Key.NumPad1))
            {
                selectedWeapon = 1;
            }
            else if (!keyWasAlreadyDown && started &&
                     (e.Key == Key.D2 || e.Key == Key.NumPad2) &&
                     currentLevel >= 13 && heavyWeaponUnlocked)
            {
                selectedWeapon = 2;
            }

            // Space springt. Nur beim ersten KeyDown auslösen, nicht durch Key-Repeat.
            if (!keyWasAlreadyDown && e.Key == Key.Space && started && jumpHeight <= 0.01)
                jumpVelocity = JumpImpulse;

            // Q = Dash in Laufrichtung (ohne Eingabe nach vorn).
            if (!keyWasAlreadyDown && e.Key == Key.Q && started && !dead && !won)
                TryDash();

            if (e.Key == Key.Escape && started)
            {
                started = false;
                isPaused = true;
                keys.Clear();
                timer.Stop();
                if (soundtrackAvailable)
                    soundtrackPlayer.Pause();
                Cursor = Cursors.Arrow;
                SetMenuTitle("PAUSE");
                if (mainMenuStartButton != null)
                    mainMenuStartButton.Content = "FORTSETZEN";
                if (resumeButton != null)
                    resumeButton.Visibility = Visibility.Visible;
                Overlay.Visibility = Visibility.Visible;
                return;
            }

            if (e.Key == Key.R && (dead || won))
            {
                Restart(levelSelector != null && levelSelector.SelectedIndex >= 0
                    ? levelSelector.SelectedIndex + 1 : 1);
                SetMenuTitle("DOOM WPF");
                Overlay.Visibility = Visibility.Visible;
            }
        }

        void Window_KeyUp(object sender, KeyEventArgs e)
        {
            keys.Remove(e.Key);
        }


        void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!started || dead || won)
                return;

            POINT mouse;
            if (!GetCursorPos(out mouse))
                return;

            Point center = GameImage.PointToScreen(
                new Point(GameImage.ActualWidth / 2,
                          GameImage.ActualHeight / 2));

            double dx = mouse.X - center.X;

            if (Math.Abs(dx) > 0)
                angle += dx * 0.006;

            CenterMouse();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.ChangedButton == MouseButton.Left &&
                started && !dead && !won)
            {
                Shoot();
            }
        }

        void Shoot()
        {
            bool heavyShot = selectedWeapon == 2 && heavyWeaponUnlocked && currentLevel >= 13;
            if (heavyShot)
            {
                if (heavyAmmo <= 0)
                {
                    MeleeAttack();
                    return;
                }
                heavyAmmo--;
            }
            else
            {
                selectedWeapon = 1;
                if (ammo <= 0)
                {
                    MeleeAttack();
                    return;
                }
                ammo--;
            }

            // Sichtbarer Projektil-Effekt; die schwere Waffe hat einen kräftigeren Schuss.
            bulletEffects.Add(new BulletEffect(heavyWeapon: heavyShot));

            Enemy target = null;
            double best = double.MaxValue;

            foreach (var enemy in enemies.Where(x => x.Alive))
            {
                double dx = enemy.X - px;
                double dy = enemy.Y - py;

                double distance = Math.Sqrt(dx * dx + dy * dy);
                double direction = Math.Atan2(dy, dx);

                double difference = Math.Atan2(
                    Math.Sin(direction - angle),
                    Math.Cos(direction - angle));

                double aimTolerance = enemy.IsBoss ? 0.18 : 0.12;

                if (Math.Abs(difference) < aimTolerance &&
                    Math.Abs(difference) < best &&
                    distance < 8 &&
                    ClearPath(px, py, enemy.X, enemy.Y))
                {
                    best = Math.Abs(difference);
                    target = enemy;
                }
            }

            if (target != null)
            {
                // Die Sturmkanone verursacht 4 Schaden je Treffer statt 1.
                // Das Power-up verdoppelt den Schaden der gerade gewählten Waffe.
                int shotDamage = heavyShot ? HeavyWeaponDamage : 1;
                if (powerUpTicks > 0)
                    shotDamage *= 2;
                target.HP -= shotDamage;
                target.HitFlashTicks = 6;

                if (target.HP <= 0)
                {
                    target.Alive = false;
                    kills++;
                }
            }


            CheckLevelComplete();
        }

        void CheckLevelComplete()
        {
            if (enemies.All(x => !x.Alive) && !changingLevel)
            {
                if (currentLevel == 3 && currentFloor == 1)
                {
                    // In Ebene 1 führt der Aufgang wie bisher zu Ebene 2.
                }
                else if (currentLevel >= 20)
                {
                    won = true;
                    EndGame("SIEG! DER ENDBOSS IST BESIEGT!");
                }
                else
                {
                    changingLevel = true;
                    int nextLevel = currentLevel + 1;
                    LoadLevel(nextLevel);
                    if (levelSelector != null)
                        levelSelector.SelectedIndex = nextLevel - 1;
                    Render();
                    EnemyText.Text = nextLevel == 3
                        ? "LEVEL 3 - EBENE 1"
                        : (nextLevel == 20 ? "LEVEL 20 - ENDBOSS!" : $"LEVEL {nextLevel}");
                }
            }

        }

        // =====================================================================
        //  DASH, NAHKAMPF UND BEWEGUNGS-EFFEKTE
        // =====================================================================
        void TryDash()
        {
            if (dashTicks > 0 || dashCooldown > 0 || stamina < DashStaminaCost)
                return;

            double fx = Math.Cos(angle), fy = Math.Sin(angle);
            double sx = -Math.Sin(angle), sy = Math.Cos(angle);
            double dx = 0, dy = 0;
            if (keys.Contains(Key.W)) { dx += fx; dy += fy; }
            if (keys.Contains(Key.S)) { dx -= fx; dy -= fy; }
            if (keys.Contains(Key.A)) { dx -= sx; dy -= sy; }
            if (keys.Contains(Key.D)) { dx += sx; dy += sy; }

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01)
            {
                dx = fx; dy = fy; len = 1;
            }

            dashDirX = dx / len;
            dashDirY = dy / len;
            dashTicks = DashDurationTicks;
            dashCooldown = DashCooldownTicks;
            stamina -= DashStaminaCost;
            staminaRegenDelay = 45;
        }

        // Kampfmesser: kurze Reichweite, breiter Schlagbereich, Rückstoß.
        void MeleeAttack()
        {
            if (meleeCooldown > 0)
                return;

            meleeCooldown = MeleeCooldownTicks;
            meleeSwingTicks = MeleeSwingDuration;

            Enemy target = null;
            double bestDistance = double.MaxValue;

            foreach (var enemy in enemies.Where(x => x.Alive))
            {
                double dx = enemy.X - px;
                double dy = enemy.Y - py;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double reach = MeleeRange + (enemy.IsBoss ? 0.35 : 0.0);
                if (distance > reach)
                    continue;

                double direction = Math.Atan2(dy, dx);
                double difference = Math.Atan2(
                    Math.Sin(direction - angle),
                    Math.Cos(direction - angle));

                if (Math.Abs(difference) < 0.55 && distance < bestDistance &&
                    ClearPath(px, py, enemy.X, enemy.Y))
                {
                    bestDistance = distance;
                    target = enemy;
                }
            }

            if (target == null)
                return;

            int damage = 2;
            if (powerUpTicks > 0)
                damage *= 2;
            target.HP -= damage;
            target.HitFlashTicks = 8;

            // Rückstoß (Bosse werden kaum zurückgeschleudert).
            double kx = target.X - px, ky = target.Y - py;
            double kLen = Math.Max(0.01, Math.Sqrt(kx * kx + ky * ky));
            double push = target.IsBoss ? 0.10 : 0.40;
            if (!IsWall(target.X + kx / kLen * push, target.Y))
                target.X += kx / kLen * push;
            if (!IsWall(target.X, target.Y + ky / kLen * push))
                target.Y += ky / kLen * push;

            if (target.HP <= 0)
            {
                target.Alive = false;
                kills++;
            }

            CheckLevelComplete();
        }

        void AddPixelLight(int x, int y, double r, double g, double b)
        {
            if (x < 0 || x >= W || y < 0 || y >= H)
                return;
            int i = (y * W + x) * 4;
            pixels[i] = (byte)Math.Clamp(pixels[i] + b, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(pixels[i + 1] + g, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(pixels[i + 2] + r, 0, 255);
        }

        void DrawThickLine(double x0, double y0, double x1, double y1, int thick, Color color)
        {
            double dx = x1 - x0, dy = y1 - y0;
            int steps = (int)Math.Max(Math.Abs(dx), Math.Abs(dy)) + 1;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                int x = (int)Math.Round(x0 + dx * t - thick / 2.0);
                int y = (int)Math.Round(y0 + dy * t - thick / 2.0);
                FillRect(x, y, thick, thick, color);
            }
        }

        // Kampfmesser mit Schwung-Animation und Leuchtspur.
        void DrawMeleeWeapon()
        {
            double lift = jumpHeight * 0.62;
            bool swinging = meleeSwingTicks > 0;
            double p = swinging ? 1.0 - (double)meleeSwingTicks / MeleeSwingDuration : 0.0;
            double ease = 1.0 - (1.0 - p) * (1.0 - p);
            double sway = swinging ? 0 : Math.Sin(frameTick * 0.05) * 1.5;
            double runBob = sprinting ? Math.Abs(Math.Sin(frameTick * 0.25)) * 3 : 0;

            double pivotX = W / 2.0 + 40 - 52 * ease;
            double pivotY = H - 52 - lift + sway + runBob;
            double a = swinging ? 0.85 - 2.1 * ease : 0.30;
            double dirX = Math.Sin(a), dirY = -Math.Cos(a);
            double perpX = -dirY, perpY = dirX;
            const double handleLen = 16, bladeLen = 58;

            // Leuchtspur der Klingenspitze.
            if (swinging)
            {
                for (int k = 1; k <= 7; k++)
                {
                    double pk = Math.Max(0, p - k * 0.05);
                    double ek = 1.0 - (1.0 - pk) * (1.0 - pk);
                    double ak = 0.85 - 2.1 * ek;
                    double pxk = W / 2.0 + 40 - 52 * ek;
                    double tipX = pxk + (handleLen + bladeLen) * Math.Sin(ak);
                    double tipY = pivotY - (handleLen + bladeLen) * Math.Cos(ak);
                    double f = (1.0 - k / 8.0) * 0.7;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                            AddPixelLight((int)tipX + ox, (int)tipY + oy, 220 * f, 235 * f, 255 * f);
                }
            }

            // Unterarm in dunklem Handschuh.
            DrawThickLine(pivotX + 12, H, pivotX, pivotY, 16, Color.FromRgb(46, 38, 32));
            DrawThickLine(pivotX + 10, H, pivotX - 1, pivotY, 8, Color.FromRgb(84, 70, 58));

            // Griff mit Wicklung.
            double hx = pivotX + dirX * handleLen, hy = pivotY + dirY * handleLen;
            DrawThickLine(pivotX, pivotY, hx, hy, 7, Color.FromRgb(72, 42, 28));
            for (int r = 3; r < handleLen; r += 4)
                DrawThickLine(pivotX + dirX * r - perpX * 3, pivotY + dirY * r - perpY * 3,
                              pivotX + dirX * r + perpX * 3, pivotY + dirY * r + perpY * 3,
                              1, Color.FromRgb(30, 18, 12));

            // Parierstange.
            DrawThickLine(hx - perpX * 9, hy - perpY * 9, hx + perpX * 9, hy + perpY * 9, 3,
                          Color.FromRgb(168, 160, 120));

            // Klinge: dunkle Kontur, Stahl, heller Schliff.
            double tx = hx + dirX * bladeLen, ty = hy + dirY * bladeLen;
            DrawThickLine(hx, hy, tx, ty, 9, Color.FromRgb(12, 14, 18));
            DrawThickLine(hx, hy, tx, ty, 6, Color.FromRgb(146, 160, 168));
            DrawThickLine(hx + perpX * 1.5, hy + perpY * 1.5, tx + perpX * 1.5, ty + perpY * 1.5,
                          2, Color.FromRgb(226, 236, 240));
            DrawThickLine(hx - perpX * 2, hy - perpY * 2, tx - perpX * 2, ty - perpY * 2,
                          1, Color.FromRgb(88, 99, 106));

            // Finger über dem Griff.
            FillRect((int)pivotX - 6, (int)pivotY - 3, 12, 7, Color.FromRgb(46, 38, 32));
            FillRect((int)pivotX - 5, (int)pivotY - 2, 10, 2, Color.FromRgb(104, 88, 72));
        }

        // Geschwindigkeitslinien: stark beim Dash, dezent beim Sprinten.
        void DrawMovementEffects()
        {
            double strength;
            int lines, minLen, maxLen;
            if (dashTicks > 0)
            {
                strength = 0.55 * dashTicks / DashDurationTicks + 0.12;
                lines = 46; minLen = 30; maxLen = 100;
            }
            else if (sprinting)
            {
                strength = 0.20;
                lines = 14; minLen = 14; maxLen = 44;
            }
            else
                return;

            for (int n = 0; n < lines; n++)
            {
                int y = Hash(n, frameTick, 201) % (H - 43);
                int len = minLen + Hash(n, frameTick, 202) % (maxLen - minLen);
                bool left = (n & 1) == 0;
                for (int s = 0; s < len; s++)
                {
                    int x = left ? s : W - 1 - s;
                    double f = (1.0 - (double)s / len) * strength;
                    AddPixelLight(x, y, 180 * f, 220 * f, 255 * f);
                }
            }
        }

        // Ausdauerleiste mit Dash-Anzeige direkt über der Statusleiste.
        void DrawStaminaBar()
        {
            const int barW = 84, barH = 5;
            int x = 8, y = H - 43 - 12;

            FillRect(x - 1, y - 1, barW + 2, barH + 2, Color.FromRgb(12, 12, 14));
            FillRect(x, y, barW, barH, Color.FromRgb(38, 40, 36));
            int fill = (int)Math.Round(barW * stamina / StaminaMax);
            Color c = staminaExhausted ? Color.FromRgb(200, 60, 45)
                    : sprinting ? Color.FromRgb(255, 214, 70)
                    : Color.FromRgb(86, 200, 120);
            if (fill > 0)
            {
                FillRect(x, y, fill, barH, c);
                FillRect(x, y, fill, 1, Color.FromRgb(240, 250, 230));
            }

            DrawHudText("STAM", x + barW + 5, y - 1, Color.FromRgb(220, 220, 207), 1);

            bool ready = dashCooldown <= 0 && stamina >= DashStaminaCost;
            FillRect(x + barW + 27, y - 2, 9, 9, Color.FromRgb(12, 12, 14));
            FillRect(x + barW + 28, y - 1, 7, 7, ready ? Color.FromRgb(90, 215, 255) : Color.FromRgb(55, 58, 60));
            DrawHudText("Q", x + barW + 39, y - 1, ready ? Color.FromRgb(90, 215, 255) : Color.FromRgb(110, 110, 104), 1);
        }

        int CurrentWeaponAmmo()
        {
            return selectedWeapon == 2 && heavyWeaponUnlocked && currentLevel >= 13
                ? heavyAmmo
                : ammo;
        }

        bool IsWall(double x, double y)
        {
            int mx = (int)x;
            int my = (int)y;

            if (my < 0 || my >= map.Length ||
                mx < 0 || mx >= map[0].Length)
                return true;

            return map[my][mx] == '1' || map[my][mx] == '3';
        }

        bool CheckFloorChange()
        {
            if (currentLevel != 3)
                return false;

            int tx = (int)px;
            int ty = (int)py;

            if (ty < 0 || ty >= map.Length ||
                tx < 0 || tx >= map[ty].Length ||
                map[ty][tx] != '2')
                return false;

            // Erst weiter, wenn alle Gegner auf dieser Ebene besiegt sind.
            if (enemies.Any(x => x.Alive))
            {
                EnemyText.Text = "Besiege erst alle Gegner!";
                return false;
            }

            LoadLevelFloor(currentFloor == 1 ? 2 : 1);
            if (levelSelector != null)
                levelSelector.SelectedIndex = 2;
            Render();
            return true;
        }

        bool ClearPath(double x1, double y1,
                       double x2, double y2)
        {
            double distance = Math.Sqrt(
                (x2 - x1) * (x2 - x1) +
                (y2 - y1) * (y2 - y1));

            int steps = Math.Max(1, (int)(distance * 20));

            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;

                if (IsWall(
                    x1 + (x2 - x1) * t,
                    y1 + (y2 - y1) * t))
                    return false;
            }

            return true;
        }

        void GameLoop(object sender, EventArgs e)
        {
            if (!started || dead || won)
                return;

            // Einfache Schwerkraft: Leertaste hebt die Kamera an und lässt sie wieder landen.
            if (jumpHeight > 0 || jumpVelocity > 0)
            {
                jumpHeight += jumpVelocity;
                jumpVelocity -= JumpGravity;
                if (jumpHeight <= 0)
                {
                    jumpHeight = 0;
                    jumpVelocity = 0;
                }
            }

            double forwardX = Math.Cos(angle);
            double forwardY = Math.Sin(angle);

            double strafeX = -Math.Sin(angle);
            double strafeY = Math.Cos(angle);

            double dx = 0, dy = 0;

            if (keys.Contains(Key.W)) { dx += forwardX; dy += forwardY; }
            if (keys.Contains(Key.S)) { dx -= forwardX; dy -= forwardY; }
            if (keys.Contains(Key.A)) { dx -= strafeX; dy -= strafeY; }
            if (keys.Contains(Key.D)) { dx += strafeX; dy += strafeY; }

            bool moving = Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001;
            if (moving)
            {
                // Diagonal laufen ist nicht schneller als geradeaus.
                double len = Math.Sqrt(dx * dx + dy * dy);
                dx /= len;
                dy /= len;
            }

            // Sprint (Shift): schneller, kostet Ausdauer. Ist sie leer, muss sie sich erst erholen.
            bool shiftDown = keys.Contains(Key.LeftShift) || keys.Contains(Key.RightShift);
            if (staminaExhausted && stamina >= 25)
                staminaExhausted = false;
            sprinting = shiftDown && moving && !staminaExhausted && dashTicks <= 0;
            if (sprinting)
            {
                stamina = Math.Max(0, stamina - 0.55);
                staminaRegenDelay = 40;
                if (stamina <= 0)
                {
                    staminaExhausted = true;
                    sprinting = false;
                }
            }
            else if (staminaRegenDelay > 0)
                staminaRegenDelay--;
            else
                stamina = Math.Min(StaminaMax, stamina + (moving ? 0.15 : 0.25));

            if (dashCooldown > 0)
                dashCooldown--;

            double moveX, moveY;
            if (dashTicks > 0)
            {
                // Dash: kurzer, abbremsender Ruck in die gewählte Richtung.
                double dashSpeed = DashSpeed * (0.55 + 0.45 * dashTicks / DashDurationTicks);
                moveX = dashDirX * dashSpeed;
                moveY = dashDirY * dashSpeed;
                dashTicks--;
            }
            else
            {
                double speed = sprinting ? SprintSpeed : WalkSpeed;
                moveX = dx * speed;
                moveY = dy * speed;
            }

            if (!IsWall(px + moveX, py))
                px += moveX;

            if (!IsWall(px, py + moveY))
                py += moveY;

            CheckSupplyBox();
            CheckPowerUp();
            UpdateTrapInteractions();
            if (dead)
                return;
            MoveEnemies();
            UpdateBossRangedAttacks();
            UpdateEnemyProjectiles();
            if (dead) return;

            if (CheckFloorChange())
                return;

            foreach (var enemy in enemies.Where(x => x.Alive))
            {
                if (trainingMode) break;
                double ex = px - enemy.X;
                double ey = py - enemy.Y;
                double distance = Math.Sqrt(ex * ex + ey * ey);
                double attackRange = enemy.IsBoss ? 0.85 : 0.55;

                // Ein Sprung kann Nahkampfangriffe kurzzeitig überbrücken.
                if (distance < attackRange && jumpHeight < 12.0 && dashTicks <= 0)
                {
                    TakeDamage(enemy.Damage);
                    if (health <= 0)
                    {
                        health = 0;
                        dead = true;
                        EndGame("GAME OVER");
                        return;
                    }
                }
            }

            UpdateBulletEffects();
            if (meleeCooldown > 0)
                meleeCooldown--;
            if (meleeSwingTicks > 0)
                meleeSwingTicks--;
            if (powerUpTicks > 0)
                powerUpTicks--;
            if (powerUpMessageTicks > 0)
                powerUpMessageTicks--;
            Render();

            HealthText.Text =
                $"Leben: {Math.Max(0, (int)health)}   Munition: {ammo}";

            string floorInfo = currentLevel == 3
                ? $" | Ebene {currentFloor}/2"
                : "";

            string bossInfo = enemies.Any(x => x.Alive && x.IsBoss)
                ? " | BOSS IM LEVEL!"
                : "";
            EnemyText.Text =
                $"Level: {currentLevel}{floorInfo} | Gegner besiegt: " +
                $"{enemies.Count(x => !x.Alive)}/{enemies.Count}{bossInfo}";

            if (supplyMessageTicks > 0)
            {
                EnemyText.Text += " | " + supplyMessage;
                supplyMessageTicks--;
            }
            else if (supplyAvailable)
            {
                double boxDx = supplyX - px;
                double boxDy = supplyY - py;
                double boxDistance = Math.Sqrt(boxDx * boxDx + boxDy * boxDy);
                if (boxDistance < 2.0)
                    EnemyText.Text += " | Versorgungskiste in der Nähe";
            }

            if (powerUpTicks > 0)
                EnemyText.Text += $" | POWER-UP: {Math.Ceiling(powerUpTicks / 60.0)} s";
            else if (powerUpMessageTicks > 0)
                EnemyText.Text += " | " + powerUpMessage;
            else if (powerUpAvailable)
            {
                double pDx = powerUpX - px;
                double pDy = powerUpY - py;
                if (Math.Sqrt(pDx * pDx + pDy * pDy) < 2.5)
                    EnemyText.Text += " | Power-up in der Nähe";
            }

            if (trapMessageTicks > 0)
            {
                EnemyText.Text += " | " + trapMessage;
                trapMessageTicks--;
            }
        }

        // =====================================================================
        //  PROZEDURALE TEXTUREN (128x128 = 4x Texel, RGBE: Rot, Grün, Blau, Leuchtkraft)
        // =====================================================================
        const int TexSize = 128;          // vorher 64
        const int TS = TexSize / 64;      // Skalierung der ursprünglichen 64er-Koordinaten
        byte[] texStone, texTorch, texLavaWall, texObsidian, texPortal, texFloor, texCeiling;
        double[] vignetteTable;
        int frameTick = 0;

        // Lava-Risse im Boden werden direkt in die perspektivische Bodenebene gezeichnet.
        int lavaCount = 0;
        readonly double[] lavaX = new double[8];
        readonly double[] lavaY = new double[8];
        readonly double[] lavaBoost = new double[8];
        readonly double[] lavaSeed = new double[8];

        void InitializeTextures()
        {
            texStone = new byte[TexSize * TexSize * 4];
            texTorch = new byte[TexSize * TexSize * 4];
            texLavaWall = new byte[TexSize * TexSize * 4];
            texObsidian = new byte[TexSize * TexSize * 4];
            texPortal = new byte[TexSize * TexSize * 4];
            texFloor = new byte[TexSize * TexSize * 4];
            texCeiling = new byte[TexSize * TexSize * 4];

            FillStone(texStone);
            BuildTorch(texTorch);
            BuildLavaWall(texLavaWall);
            BuildObsidian(texObsidian);
            BuildPortal(texPortal);
            BuildFloor(texFloor);
            BuildCeiling(texCeiling);

            vignetteTable = new double[W];
            for (int x = 0; x < W; x++)
                vignetteTable[x] = 1.0 - 0.30 * Math.Abs((double)x / W - 0.5) * 2.0;
        }

        static int Hash(int x, int y, int seed = 0)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1274126177 + 0x5bd1e995;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return h & 0x7fffffff;
            }
        }

        static double Noise01(int x, int y, int seed = 0) => (Hash(x, y, seed) % 1024) / 1023.0;

        // Kachelbares Value-Noise; "period" ist die Gitterlänge, nach der es sich wiederholt.
        static double ValueNoise(double x, double y, int period, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
            int ya = ((y0 % period) + period) % period, yb = (ya + 1) % period;
            double a = Noise01(xa, ya, seed), b = Noise01(xb, ya, seed);
            double c = Noise01(xa, yb, seed), d = Noise01(xb, yb, seed);
            double top = a + (b - a) * fx;
            double bottom = c + (d - c) * fx;
            return top + (bottom - top) * fy;
        }

        static void PutTex(byte[] t, int x, int y, double r, double g, double b, int e = 0)
        {
            int i = (y * TexSize + x) * 4;
            t[i] = (byte)Math.Clamp(r, 0, 255);
            t[i + 1] = (byte)Math.Clamp(g, 0, 255);
            t[i + 2] = (byte)Math.Clamp(b, 0, 255);
            t[i + 3] = (byte)Math.Clamp(e, 0, 255);
        }

        // Prägt aus der Helligkeit ein Höhenrelief ein (Licht von links oben): Fugen wirken
        // vertieft, Kanten bekommen eine helle und eine dunkle Seite. Leuchtende Texel bleiben
        // unverändert. Die Textur ist kachelbar, deshalb läuft die Nachbarschaft ringförmig um.
        static void ApplyRelief(byte[] t, double strength)
        {
            int n = TexSize;
            var h = new double[n * n];
            for (int i = 0; i < n * n; i++)
                h[i] = 0.30 * t[i * 4] + 0.59 * t[i * 4 + 1] + 0.11 * t[i * 4 + 2];

            var s = new double[n * n];
            for (int y = 0; y < n; y++)
            {
                int ym = (y + n - 1) % n, yp = (y + 1) % n;
                for (int x = 0; x < n; x++)
                {
                    int xm = (x + n - 1) % n, xp = (x + 1) % n;
                    s[y * n + x] = (4 * h[y * n + x] + h[y * n + xm] + h[y * n + xp]
                                    + h[ym * n + x] + h[yp * n + x]) / 8.0;
                }
            }

            for (int y = 0; y < n; y++)
            {
                int ym = (y + n - 1) % n, yp = (y + 1) % n;
                for (int x = 0; x < n; x++)
                {
                    int xm = (x + n - 1) % n, xp = (x + 1) % n;
                    int i = (y * n + x) * 4;
                    if (t[i + 3] > 0) continue;

                    double gx = s[y * n + xp] - s[y * n + xm];
                    double gy = s[yp * n + x] - s[ym * n + x];
                    double shade = Math.Clamp(1.0 + (gx + gy) * strength, 0.62, 1.45);
                    t[i] = (byte)Math.Clamp(t[i] * shade, 0, 255);
                    t[i + 1] = (byte)Math.Clamp(t[i + 1] * shade, 0, 255);
                    t[i + 2] = (byte)Math.Clamp(t[i + 2] * shade, 0, 255);
                }
            }
        }

        // Versetztes Ziegelmauerwerk mit unregelmäßigen Kanten, Moos, Porenlöchern, feinen Rissen
        // und eingeprägtem Relief.
        static void FillStone(byte[] t)
        {
            int bh = 16 * TS, bw = 32 * TS, mw = 2 * TS;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int row = y / bh;
                    int ly = y % bh;
                    int sx = (x + (row % 2) * (bw / 2)) % TexSize;
                    int lx = sx % bw;
                    int brick = sx / bw;
                    double tint = 0.82 + 0.30 * Noise01(brick, row, 3);
                    double grain = (Noise01(x, y, 11) - 0.5) * 26;
                    double soft = (ValueNoise(x / (8.0 * TS), y / (8.0 * TS), 8, 4) - 0.5) * 34;
                    double mid = (ValueNoise(x / (4.0 * TS), y / (4.0 * TS), 16, 5) - 0.5) * 18;

                    double r = 150 * tint + grain + soft + mid;
                    double g = 128 * tint + grain * 0.9 + soft * 0.85 + mid * 0.9;
                    double b = 104 * tint + grain * 0.8 + soft * 0.7 + mid * 0.8;

                    // Ausgebrochene, unregelmäßige Ziegelkanten.
                    double jit = (ValueNoise(x / (2.0 * TS), y / (2.0 * TS), 32, 33) - 0.5) * 1.6 * TS;

                    if (ly + jit < mw || lx + jit < mw)
                    {
                        double m = 44 + grain * 0.5 + soft * 0.3;
                        r = m + 8; g = m + 3; b = m;
                    }
                    else
                    {
                        if (ly + jit < mw + TS || lx + jit < mw + TS) { r += 22; g += 20; b += 18; }

                        int dBottom = Math.Min(bh - 1 - ly, bw - 1 - lx);
                        if (dBottom < 3 * TS)
                        {
                            double k = (3 * TS - dBottom) / (3.0 * TS) * 30;
                            r -= k; g -= k * 0.95; b -= k * 0.9;
                        }

                        double pit = ValueNoise(x / (2.0 * TS), y / (2.0 * TS), 32, 14);
                        if (pit > 0.80)
                        {
                            double k = Math.Min(1.0, (pit - 0.80) * 3.0);
                            r *= 1 - 0.5 * k; g *= 1 - 0.5 * k; b *= 1 - 0.5 * k;
                        }

                        double moss = ValueNoise(x / (8.0 * TS), y / (8.0 * TS), 8, 21) * 0.75
                                    + ValueNoise(x / (2.0 * TS), y / (2.0 * TS), 32, 22) * 0.25;
                        if (y > 38 * TS && moss > 0.62)
                        {
                            double k = Math.Min(1.0, (moss - 0.62) * 2.2);
                            r = r * (1 - k) + 60 * k;
                            g = g * (1 - k) + 92 * k;
                            b = b * (1 - k) + 44 * k;
                        }

                        int ch = Hash(brick, row, 31);
                        if (ch % 4 == 0 && ly > mw)
                        {
                            double wob = (ValueNoise(x / (2.0 * TS), y / (2.0 * TS), 32, 34) - 0.5) * 5.0;
                            if (Math.Abs((lx - ly) - (6 + ch % 14) * TS + wob) < 0.8 * TS)
                            {
                                r *= 0.5; g *= 0.5; b *= 0.5;
                            }
                        }
                    }
                    PutTex(t, x, y, r, g, b);
                }
            }
            ApplyRelief(t, 0.0045);
        }

        // Wandfackel: Eisenhalterung mit Rundung, tropfenförmige Flamme mit weichem Rand und Lichthof.
        static void BuildTorch(byte[] t)
        {
            FillStone(t);
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int i = (y * TexSize + x) * 4;
                    // Koordinaten im ursprünglichen 64er-Raster.
                    double u = (x + 0.5) / TS, v = (y + 0.5) / TS;
                    double dx = u - 32, dy = v - 21;
                    double halo = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy) / 26.0);
                    halo *= halo;
                    t[i] = (byte)Math.Min(255, t[i] + 150 * halo);
                    t[i + 1] = (byte)Math.Min(255, t[i + 1] + 70 * halo);
                    t[i + 2] = (byte)Math.Min(255, t[i + 2] + 10 * halo);

                    if (u >= 30 && u < 34 && v >= 30 && v < 47)
                    {
                        double cyl = 0.55 + 0.75 * Math.Sin((u - 30) / 4.0 * Math.PI);
                        PutTex(t, x, y, 34 * cyl + 6, 32 * cyl + 6, 38 * cyl + 8);
                    }
                    if (v >= 28 && v < 31 && u >= 26 && u < 38)
                    {
                        double topLight = v < 29 ? 1.35 : 1.0;
                        PutTex(t, x, y, 52 * topLight, 50 * topLight, 54 * topLight);
                    }
                    if (v >= 27 && v < 28 && u >= 27 && u < 37) PutTex(t, x, y, 70, 66, 64);

                    if (v < 27 && v >= 5)
                    {
                        double h = (27 - v) / 22.0;
                        double halfW = 6.5 * Math.Sin(Math.PI * Math.Pow(h, 0.65)) * (1 - h * 0.35);
                        double off = Math.Abs(u - 32 + Math.Sin(h * 5) * 1.2);
                        if (off < halfW)
                        {
                            if (off < halfW * 0.40) PutTex(t, x, y, 255, 246, 170, 255);
                            else if (off < halfW * 0.72) PutTex(t, x, y, 255, 182, 42, 255);
                            else PutTex(t, x, y, 255, 112, 18, 255);
                        }
                        else if (halfW > 0.5 && off < halfW * 1.25)
                        {
                            double f = 0.65 * (1 - (off - halfW) / (halfW * 0.25));
                            PutTex(t, x, y,
                                t[i] * (1 - f) + 255 * f,
                                t[i + 1] * (1 - f) + 90 * f,
                                t[i + 2] * (1 - f) + 14 * f,
                                (int)(170 * f));
                        }
                    }
                }
            }
        }

        // Wand mit glühenden Lavaadern (dicke Hauptadern plus feine Nebenadern) im Mauerwerk.
        static void BuildLavaWall(byte[] t)
        {
            FillStone(t);
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int i = (y * TexSize + x) * 4;
                    double r = t[i] * 0.72, g = t[i + 1] * 0.72, b = t[i + 2] * 0.72;
                    double warp = ValueNoise(x / (16.0 * TS), y / (16.0 * TS), 4, 41) * 1.2;
                    double n = ValueNoise(x / (8.0 * TS) + warp, y / (8.0 * TS), 8, 51);
                    double ridge = Math.Abs(n - 0.5);
                    double n2 = ValueNoise(x / (4.0 * TS) + warp * 0.5, y / (4.0 * TS), 16, 52);
                    double ridge2 = Math.Abs(n2 - 0.5);

                    if (ridge < 0.028)
                    {
                        double k = ridge / 0.028;
                        PutTex(t, x, y, 255, 235 - 125 * k, 120 - 100 * k, 255);
                    }
                    else if (ridge < 0.075)
                    {
                        double f = (1 - (ridge - 0.028) / 0.047) * 0.8;
                        PutTex(t, x, y, r * (1 - f) + 225 * f, g * (1 - f) + 55 * f, b * (1 - f) + 12 * f, (int)(f * 150));
                    }
                    else if (ridge2 < 0.012)
                    {
                        PutTex(t, x, y, 255, 170, 60, 220);
                    }
                    else if (ridge2 < 0.03)
                    {
                        double f = (1 - (ridge2 - 0.012) / 0.018) * 0.55;
                        PutTex(t, x, y, r * (1 - f) + 210 * f, g * (1 - f) + 70 * f, b * (1 - f) + 20 * f, (int)(f * 110));
                    }
                    else
                    {
                        PutTex(t, x, y, r, g, b);
                    }
                }
            }
        }

        // Bossraum: Obsidianplatten mit Kristallfacetten, glühenden Fugen und feinen Lavaadern.
        static void BuildObsidian(byte[] t)
        {
            int slab = 32 * TS;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int lx = x % slab, ly = y % slab;
                    double n = Noise01(x, y, 61);
                    double facet = ValueNoise(x / (4.0 * TS), y / (4.0 * TS), 16, 64);
                    double bright = 26 + 10 * Noise01(x / slab, y / slab, 62) + (n - 0.5) * 8 + (facet - 0.5) * 16;
                    double sheen = ((x + y) % slab < 3 * TS) ? 18 : 0;
                    double r = bright * 1.1 + sheen;
                    double g = bright * 0.85 + sheen * 0.9;
                    double b = bright * 1.4 + sheen * 1.2;
                    int e = 0;

                    if (lx < 2 * TS || ly < 2 * TS)
                    {
                        r = 255; g = 70 + 40 * Noise01(x, y, 63); b = 18; e = 255;
                    }
                    else
                    {
                        if (lx < 4 * TS || ly < 4 * TS)
                        {
                            double fall = 1.0 - (Math.Min(lx, ly) - 2.0 * TS) / (2.0 * TS);
                            r += 70 * fall; g += 10 * fall; e = (int)(90 * fall);
                        }
                        double v = Math.Abs(ValueNoise(x / (8.0 * TS), y / (8.0 * TS), 8, 71) - 0.5);
                        if (v < 0.02) { r = 255; g = 110; b = 25; e = 200; }
                    }
                    PutTex(t, x, y, r, g, b, e);
                }
            }
            ApplyRelief(t, 0.003);
        }

        // Aufgang: leuchtendes Portal mit weich auslaufenden, nach oben laufenden Pfeilen.
        static void BuildPortal(byte[] t)
        {
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    double u = (x + 0.5) / TS, v = (y + 0.5) / TS;
                    bool border = u < 3 || u >= 61 || v < 3 || v >= 61;
                    bool rim = (u >= 3 && u < 4) || (u >= 60 && u < 61) || (v >= 3 && v < 4) || (v >= 60 && v < 61);
                    if (border)
                    {
                        double n = (Noise01(x, y, 101) - 0.5) * 14;
                        PutTex(t, x, y, 34 + n, 52 + n, 60 + n);
                    }
                    else if (rim)
                    {
                        PutTex(t, x, y, 160, 255, 255, 255);
                    }
                    else
                    {
                        double w = (v + Math.Abs(u - 32)) % 16;
                        double band = w < 4 ? 1.0 : Math.Max(0.0, 1.0 - (w - 4) / 2.5);
                        band = Math.Clamp(band + (Noise01(x, y, 102) - 0.5) * 0.12, 0, 1);
                        PutTex(t, x, y, 20 + 130 * band, 110 + 145 * band, 140 + 115 * band, (int)(120 + 135 * band));
                    }
                }
            }
        }

        // Steinplatten mit Fugen, abgenutzten Kanten, Poren und feinen Rissen plus Relief.
        static void BuildFloor(byte[] t)
        {
            int slabSize = 32 * TS;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int slabX = x / slabSize, slabY = y / slabSize;
                    int lx = x % slabSize, ly = y % slabSize;
                    double tint = 0.80 + 0.32 * Noise01(slabX, slabY, 81);
                    double grain = (Noise01(x, y, 82) - 0.5) * 20;
                    double soft = (ValueNoise(x / (8.0 * TS), y / (8.0 * TS), 8, 83) - 0.5) * 30;
                    double fine = (ValueNoise(x / (2.0 * TS), y / (2.0 * TS), 32, 85) - 0.5) * 14;
                    double r = 104 * tint + grain + soft + fine;
                    double g = 98 * tint + grain * 0.95 + soft * 0.9 + fine * 0.9;
                    double b = 92 * tint + grain * 0.9 + soft * 0.8 + fine * 0.8;

                    if (lx < 2 * TS || ly < 2 * TS)
                    {
                        double j = 26 + grain * 0.4;
                        r = g = b = j;
                    }
                    else
                    {
                        int edge = Math.Min(Math.Min(lx, ly), Math.Min(slabSize - 1 - lx, slabSize - 1 - ly));
                        if (edge < 5 * TS)
                        {
                            double k = (5.0 * TS - edge) / (5.0 * TS) * 0.22;
                            r *= 1 - k; g *= 1 - k; b *= 1 - k;
                        }
                        if (lx < 3 * TS || ly < 3 * TS) { r += 14; g += 14; b += 14; }

                        int ch = Hash(slabX, slabY, 84);
                        if (ch % 5 == 0 && lx > 3 * TS && lx < 28 * TS)
                        {
                            double wob = (ValueNoise(x / (2.0 * TS), y / (2.0 * TS), 32, 86) - 0.5) * 4.0;
                            if (Math.Abs(lx + ly - 32 * TS - (ch % 7 - 3) * TS + wob) < 1.2)
                            {
                                r *= 0.5; g *= 0.5; b *= 0.5;
                            }
                        }
                    }
                    PutTex(t, x, y, r, g, b);
                }
            }
            ApplyRelief(t, 0.004);
        }

        // Gebürstete Metallplatten mit plastischen Nieten, Lüftungsgittern und Deckenlampen.
        static void BuildCeiling(byte[] t)
        {
            int pw = 32 * TS;
            int[] rv = { 5 * TS, 26 * TS };
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    int panelX = x / pw, panelY = y / pw;
                    int lx = x % pw, ly = y % pw;
                    double tint = 0.85 + 0.25 * Noise01(panelX, panelY, 91);
                    double grain = (Noise01(x, y, 92) - 0.5) * 12;
                    double brushed = (Noise01(x / 8, y, 94) - 0.5) * 9;
                    double r = 58 * tint + grain + brushed;
                    double g = 58 * tint + grain + brushed;
                    double b = 68 * tint + (grain + brushed) * 1.1;
                    int e = 0;
                    int ph = Hash(panelX, panelY, 93);

                    if (lx < 2 * TS || ly < 2 * TS) { r = 18; g = 16; b = 22; }
                    else
                    {
                        if (lx < 3 * TS || ly < 3 * TS) { r += 12; g += 12; b += 12; }
                        foreach (int cx in rv)
                        {
                            foreach (int cy in rv)
                            {
                                int ddx = lx - cx, ddy = ly - cy;
                                if (ddx * ddx + ddy * ddy <= 2 * TS * TS)
                                {
                                    double hl = (ddx + ddy) < 0 ? 1.3 : 0.8;
                                    r = 110 * hl; g = 108 * hl; b = 120 * hl;
                                }
                            }
                        }

                        if (ph % 6 == 0 && lx >= 8 * TS && lx < 24 * TS && ly >= 8 * TS && ly < 24 * TS)
                        {
                            if (ly % (3 * TS) < TS) { r = 12; g = 12; b = 16; }
                            else { r = 70; g = 70; b = 80; }
                        }
                        else if (ph % 9 == 1 && lx >= 10 * TS && lx < 22 * TS && ly >= 10 * TS && ly < 22 * TS)
                        {
                            double edgeD = Math.Min(Math.Min(lx - 10 * TS, 21 * TS - lx),
                                                    Math.Min(ly - 10 * TS, 21 * TS - ly));
                            double glow = Math.Clamp(edgeD / (3.0 * TS), 0, 1);
                            r = 235 - 55 * (1 - glow); g = 228 - 55 * (1 - glow); b = 190 - 50 * (1 - glow); e = 255;
                        }
                    }
                    PutTex(t, x, y, r, g, b, e);
                }
            }
            ApplyRelief(t, 0.003);
        }

        // =====================================================================
        //  RENDER
        // =====================================================================
        void Render()
        {
            frameTick++;
            foreach (var en in enemies)
                if (en.HitFlashTicks > 0) en.HitFlashTicks--;

            int horizon = GetViewHorizon();
            PrepareLavaFrameData();
            RenderFloorAndCeiling(horizon);

            for (int x = 0; x < W; x++)
            {
                double cameraX = 2.0 * x / W - 1.0;

                double rayX = Math.Cos(angle) -
                              Math.Sin(angle) * cameraX * 0.66;

                double rayY = Math.Sin(angle) +
                              Math.Cos(angle) * cameraX * 0.66;

                int mapX = (int)px;
                int mapY = (int)py;

                double deltaX = Math.Abs(1.0 / rayX);
                double deltaY = Math.Abs(1.0 / rayY);

                double sideX = rayX < 0
                    ? (px - mapX) * deltaX
                    : (mapX + 1.0 - px) * deltaX;

                double sideY = rayY < 0
                    ? (py - mapY) * deltaY
                    : (mapY + 1.0 - py) * deltaY;

                int stepX = rayX < 0 ? -1 : 1;
                int stepY = rayY < 0 ? -1 : 1;

                int side = 0;

                for (int n = 0; n < 80; n++)
                {
                    if (sideX < sideY)
                    {
                        sideX += deltaX;
                        mapX += stepX;
                        side = 0;
                    }
                    else
                    {
                        sideY += deltaY;
                        mapY += stepY;
                        side = 1;
                    }

                    if (mapY < 0 || mapY >= map.Length ||
                        mapX < 0 || mapX >= map[0].Length ||
                        (map[mapY][mapX] == '1' ||
                          map[mapY][mapX] == '2' ||
                          map[mapY][mapX] == '3'))
                        break;
                }

                char cell = (mapY >= 0 && mapY < map.Length && mapX >= 0 && mapX < map[0].Length)
                    ? map[mapY][mapX] : '1';

                double distance = side == 0
                    ? sideX - deltaX
                    : sideY - deltaY;

                distance = Math.Max(0.1, distance);
                depth[x] = distance;

                int lineHeight = Math.Max(1, (int)(H / distance));
                int start = Math.Max(0, horizon - lineHeight / 2);
                int end = Math.Min(H - 1, horizon + lineHeight / 2);

                // Trefferpunkt an der Wand => Textur haftet an der Wand statt am Bildschirm.
                double wallX = side == 0 ? py + distance * rayY : px + distance * rayX;
                wallX -= Math.Floor(wallX);
                int texX = (int)(wallX * TexSize);
                if (side == 0 && rayX > 0) texX = TexSize - 1 - texX;
                if (side == 1 && rayY < 0) texX = TexSize - 1 - texX;
                texX = Math.Clamp(texX, 0, TexSize - 1);

                int kind;
                if (cell == '2') kind = 4;
                else if (cell == '3') kind = 3;
                else
                {
                    int h = Hash(mapX, mapY, currentLevel) % 100;
                    kind = h < 8 ? 1 : h < 16 ? 2 : 0;
                }

                byte[] tex = kind switch
                {
                    1 => texTorch,
                    2 => texLavaWall,
                    3 => texObsidian,
                    4 => texPortal,
                    _ => texStone
                };

                double pulse = kind switch
                {
                    1 => 0.80 + 0.20 * Math.Sin(frameTick * 0.31 + mapX * 1.7 + mapY * 2.3)
                              + 0.07 * Math.Sin(frameTick * 0.83 + mapX),
                    2 => 0.78 + 0.22 * Math.Sin(frameTick * 0.07 + mapX * 0.9 + mapY * 1.3),
                    3 => 0.72 + 0.28 * Math.Sin(frameTick * 0.05 + mapY * 0.6 + mapX * 0.4),
                    4 => 0.85 + 0.15 * Math.Sin(frameTick * 0.12),
                    _ => 1.0
                };
                int scroll = kind == 4 ? frameTick * TS / 2 : 0;

                double vig = 1.0 - 0.26 * cameraX * cameraX;
                double light = Math.Clamp(1.18 / (1.0 + distance * 0.24), 0.12, 1.0)
                               * (side == 1 ? 0.74 : 1.0) * vig;

                double wallTop = horizon - lineHeight / 2.0;
                double texStep = (double)TexSize / lineHeight;
                double texPos = (start - wallTop) * texStep;

                for (int y = start; y <= end; y++)
                {
                    int texY = ((int)texPos + scroll) & (TexSize - 1);
                    texPos += texStep;
                    int ti = (texY * TexSize + texX) * 4;

                    double r = tex[ti], g = tex[ti + 1], b = tex[ti + 2];
                    int e = tex[ti + 3];

                    // Leichte Eckenabdunklung oben und unten an der Wand.
                    double edge = Math.Abs((y - wallTop) / lineHeight - 0.5) * 2.0;
                    double ao = 1.0 - 0.28 * edge * edge * edge * edge;
                    double k = light * ao;

                    if (e > 0)
                    {
                        double em = e / 255.0;
                        double glowK = (0.55 + 0.45 * light) * pulse;
                        k = k * (1 - em) + glowK * em;
                    }

                    int i = (y * W + x) * 4;
                    pixels[i] = (byte)Math.Clamp(b * k, 0, 255);
                    pixels[i + 1] = (byte)Math.Clamp(g * k, 0, 255);
                    pixels[i + 2] = (byte)Math.Clamp(r * k, 0, 255);
                    pixels[i + 3] = 255;
                }
            }

            DrawSupplyBox();
            DrawPowerUp();
            DrawTraps();
            DrawEnemies();
            DrawEnemyProjectiles();
            DrawPistol();
            DrawBulletEffects();
            DrawMovementEffects();
            DrawHudBar();
            DrawStaminaBar();

            bitmap.WritePixels(
                new Int32Rect(0, 0, W, H),
                pixels, W * 4, 0);
        }

        // Perspektivisch korrekte Boden- und Deckenprojektion (Floor Casting).
        void RenderFloorAndCeiling(int horizon)
        {
            double dirX = Math.Cos(angle), dirY = Math.Sin(angle);
            double plX = -Math.Sin(angle) * 0.66, plY = Math.Cos(angle) * 0.66;
            double r0x = dirX - plX, r0y = dirY - plY;
            double dXpp = plX * 2.0 / W, dYpp = plY * 2.0 / W;

            for (int y = 0; y < H; y++)
            {
                bool ceiling = y < horizon;
                int p = Math.Max(1, ceiling ? horizon - y : y - horizon);
                double rowDist = 0.5 * H / p;
                byte[] tex = ceiling ? texCeiling : texFloor;
                double light = Math.Clamp(1.22 - rowDist * 0.115, 0.10, 1.0);
                if (ceiling) light *= 0.9;

                double fx = px + rowDist * r0x;
                double fy = py + rowDist * r0y;
                double stepFx = rowDist * dXpp;
                double stepFy = rowDist * dYpp;

                for (int x = 0; x < W; x++, fx += stepFx, fy += stepFy)
                {
                    int tx = (int)(fx * TexSize) & (TexSize - 1);
                    int ty = (int)(fy * TexSize) & (TexSize - 1);
                    int ti = (ty * TexSize + tx) * 4;

                    double vig = vignetteTable[x];
                    double k = light * vig;
                    double r = tex[ti] * k, g = tex[ti + 1] * k, b = tex[ti + 2] * k;

                    if (ceiling)
                    {
                        if (tex[ti + 3] > 0)
                        {
                            double em = tex[ti + 3] / 255.0;
                            r = r * (1 - em) + tex[ti] * vig * em;
                            g = g * (1 - em) + tex[ti + 1] * vig * em;
                            b = b * (1 - em) + tex[ti + 2] * vig * em;
                        }
                    }
                    else if (lavaCount > 0)
                    {
                        ApplyLavaFloor(ref r, ref g, ref b, fx, fy, rowDist);
                    }

                    int i = (y * W + x) * 4;
                    pixels[i] = (byte)Math.Clamp(b, 0, 255);
                    pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
                    pixels[i + 2] = (byte)Math.Clamp(r, 0, 255);
                    pixels[i + 3] = 255;
                }
            }
        }

        void PrepareLavaFrameData()
        {
            lavaCount = 0;
            foreach (var trap in traps)
            {
                if (trap.Kind != TrapKind.LavaCrack || lavaCount >= lavaX.Length)
                    continue;
                lavaX[lavaCount] = trap.X;
                lavaY[lavaCount] = trap.Y;
                lavaBoost[lavaCount] = trap.ActiveTicks > 0 ? 1.7 : 1.0;
                lavaSeed[lavaCount] = trap.X * 3.1 + trap.Y * 7.7;
                lavaCount++;
            }
        }

        // Glühende, verzweigte Lavarisse mit verbranntem Rand und warmem Lichthof.
        void ApplyLavaFloor(ref double r, ref double g, ref double b, double fx, double fy, double rowDist)
        {
            for (int t = 0; t < lavaCount; t++)
            {
                double dx = fx - lavaX[t], dy = fy - lavaY[t];
                double d2 = dx * dx + dy * dy;
                if (d2 > 1.7) continue;

                double rad = Math.Sqrt(d2);
                double boost = lavaBoost[t];
                double seed = lavaSeed[t];
                double pulse = (0.78 + 0.22 * Math.Sin(frameTick * 0.09 + seed)) * boost;

                // Verbrannter Boden um den Riss.
                if (rad < 0.68)
                {
                    double scorch = 1 - 0.55 * (1 - rad / 0.68);
                    r *= scorch; g *= scorch; b *= scorch;
                }

                // Warmes Umgebungslicht der Lava.
                double glow = Math.Max(0, 1 - rad / 1.3);
                glow = glow * glow * 0.60 * pulse;
                r += 170 * glow; g += 62 * glow; b += 12 * glow;

                if (rad > 0.68) continue;

                double ang = Math.Atan2(dy, dx);
                double best = 9;
                for (int k = 0; k < 7; k++)
                {
                    double a0 = k * (Math.PI * 2 / 7) + seed * 0.7;
                    double len = 0.36 + 0.28 * Math.Abs(Math.Sin(seed * 12.9898 + k * 78.233));
                    double wig = 0.34 * Math.Sin(rad * 13 + k * 2.1 + seed) * (0.3 + rad);
                    double da = Math.IEEERemainder(ang - a0 - wig, Math.PI * 2);
                    double perp = rad * Math.Abs(da);
                    if (rad > len) perp += (rad - len) * 3.0;
                    if (perp < best) best = perp;
                }

                double width = (0.014 + 0.022 * (1 - rad / 0.68)) * (boost > 1 ? 1.5 : 1.0)
                               * (1 + rowDist * 0.07);
                if (rad < 0.13) best = 0; // glühender Kern

                if (best < width)
                {
                    double h = best / width;
                    double heat = Math.Min(1.35, 0.85 + 0.2 * pulse);
                    double flicker = 1.0;
                    if (rad < 0.13)
                        flicker = 0.8 + 0.4 * Noise01((int)(fx * 48), (int)(fy * 48), frameTick / 6);
                    r = Math.Min(255, 255 * heat);
                    g = Math.Clamp((235 - 135 * h) * heat * flicker, 0, 255);
                    b = Math.Clamp((110 - 95 * h) * heat, 0, 255);
                }
                else if (best < width * 3.6)
                {
                    double f = (1 - (best - width) / (width * 2.6)) * 0.85;
                    r = r * (1 - f) + 215 * f * Math.Min(1.2, pulse + 0.2);
                    g = g * (1 - f) + 48 * f;
                    b = b * (1 - f) + 10 * f;
                }
            }
        }

        // Fügt farbiges Licht additiv hinzu (z. B. Auren, Augenglühen, Lava-Dunst).
        void AddGlow(int cx, int cy, int radius, Color c, double strength, double dist)
        {
            if (radius < 1 || strength <= 0) return;
            int r2 = radius * radius;
            for (int ay = -radius; ay <= radius; ay++)
            {
                int y = cy + ay;
                if (y < 0 || y >= H) continue;
                for (int ax = -radius; ax <= radius; ax++)
                {
                    int x = cx + ax;
                    if (x < 0 || x >= W || dist >= depth[x]) continue;
                    int d2 = ax * ax + ay * ay;
                    if (d2 > r2) continue;
                    double f = 1.0 - Math.Sqrt(d2) / radius;
                    f = f * f * strength;
                    int i = (y * W + x) * 4;
                    pixels[i] = (byte)Math.Min(255, pixels[i] + c.B * f);
                    pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + c.G * f);
                    pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + c.R * f);
                }
            }
        }

        // Aufsteigende Funken und Hitzedunst über einem Lavariss.
        void DrawLavaEmbers(Trap trap, int screenX, int horizon, double distance, double difference, bool active)
        {
            double perp = Math.Max(0.15, distance * Math.Cos(difference));
            double scale = H / perp;
            int floorY = (int)(horizon + 0.5 * scale);
            double seed = trap.X * 3.1 + trap.Y * 7.7;

            int hazeR = (int)Math.Min(90, scale * 0.45);
            AddGlow(screenX, floorY - (int)(scale * 0.04), hazeR,
                    Color.FromRgb(255, 110, 25), active ? 0.42 : 0.20, distance);

            int count = active ? 16 : 7;
            for (int i = 0; i < count; i++)
            {
                double phase = (frameTick * (active ? 0.035 : 0.018) + i / (double)count + i * 0.37 + seed * 0.13) % 1.0;
                double lift = phase * scale * (active ? 0.80 : 0.40);
                double sway = Math.Sin(i * 2.7 + phase * 7.0 + seed) * scale * 0.20;
                int ex = screenX + (int)sway;
                int ey = floorY - (int)lift;
                if (ex < 0 || ex >= W || ey < 0 || ey >= H || distance >= depth[ex]) continue;

                double fade = 1.0 - phase;
                Color c = Color.FromRgb(255, (byte)(60 + 170 * fade), (byte)(10 + 40 * fade * fade));
                int s = Math.Max(1, (int)(scale * 0.012));
                for (int yy = 0; yy < s; yy++)
                    for (int xx = 0; xx < s; xx++)
                        SetPixel(ex + xx, ey + yy, c);
            }
        }


        void UpdateBulletEffects()
        {
            // Rund 7-8 Frames Fluganimation bei 60 FPS.
            for (int i = bulletEffects.Count - 1; i >= 0; i--)
            {
                bulletEffects[i].Progress += 0.14;
                if (bulletEffects[i].Progress >= 1.0)
                    bulletEffects.RemoveAt(i);
            }
        }

        void SetPixel(int x, int y, Color color)
        {
            if (x < 0 || x >= W || y < 0 || y >= H)
                return;

            int i = (y * W + x) * 4;
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        void FillRect(int x, int y, int width, int height, Color color)
        {
            int left = Math.Max(0, x);
            int top = Math.Max(0, y);
            int right = Math.Min(W, x + width);
            int bottom = Math.Min(H, y + height);

            for (int py = top; py < bottom; py++)
            {
                for (int px = left; px < right; px++)
                    SetPixel(px, py, color);
            }
        }

        void DrawBulletEffects()
        {
            foreach (var bullet in bulletEffects)
            {
                double t = Math.Clamp(bullet.Progress, 0.0, 1.0);

                // Die schwere Sturmkanone feuert ein größeres, helleres Projektil.
                double startX = bullet.HeavyWeapon ? W / 2.0 + 8 : W / 2.0 + 18;
                double startY = (bullet.HeavyWeapon ? H - 101 : H - 79) - jumpHeight * 0.62;
                double endX = W / 2.0;
                double endY = H / 2.0 + 5;

                // Kleine Beschleunigungs-/Perspektivkurve nach vorn.
                double travel = t * t;
                int cx = (int)Math.Round(startX + (endX - startX) * travel);
                int cy = (int)Math.Round(startY + (endY - startY) * travel);

                // Die Kugel wird mit zunehmender Flugweite sichtbar kleiner.
                int radius = Math.Max(1, (int)Math.Round((bullet.HeavyWeapon ? 9.0 : 6.0) * (1.0 - t) + 0.5));

                // Kurzer warmer Leuchthof hinter der Kugel.
                for (int y = cy - radius - 2; y <= cy + radius + 2; y++)
                {
                    for (int x = cx - radius - 2; x <= cx + radius + 2; x++)
                    {
                        double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d <= radius + 2 && d > radius)
                            SetPixel(x, y, bullet.HeavyWeapon
                                ? Color.FromRgb(255, 90, 25)
                                : Color.FromRgb(220, 75, 18));
                    }
                }

                // Kern der Kugel: gelb-weiß vorn, orange außen.
                for (int y = cy - radius; y <= cy + radius; y++)
                {
                    for (int x = cx - radius; x <= cx + radius; x++)
                    {
                        int dx = x - cx;
                        int dy = y - cy;
                        if (dx * dx + dy * dy > radius * radius)
                            continue;

                        Color color = (Math.Abs(dx) + Math.Abs(dy) <= Math.Max(1, radius / 2))
                            ? (bullet.HeavyWeapon ? Color.FromRgb(255, 255, 225) : Color.FromRgb(255, 255, 185))
                            : (bullet.HeavyWeapon ? Color.FromRgb(255, 112, 25) : Color.FromRgb(255, 185, 35));
                        SetPixel(x, y, color);
                    }
                }

                // Mündungsfeuer erscheint nur direkt beim Abfeuern.
                if (t < 0.17)
                {
                    FillRect((int)startX - 2, (int)startY - 5, 5, 8,
                        Color.FromRgb(255, 230, 95));
                    SetPixel((int)startX - 4, (int)startY - 1,
                        Color.FromRgb(255, 110, 15));
                    SetPixel((int)startX + 3, (int)startY - 1,
                        Color.FromRgb(255, 110, 15));
                }
            }
        }

        void DrawPistol()
        {
            // Die Methode zeichnet die aktuell gewählte Waffe.
            // Ohne Munition hält der Spieler stattdessen das Kampfmesser.
            if (CurrentWeaponAmmo() <= 0)
            {
                DrawMeleeWeapon();
                return;
            }

            if (selectedWeapon == 2 && heavyWeaponUnlocked && currentLevel >= 13)
            {
                DrawHeavyWeapon();
                return;
            }

            // Detailliertere Pixel-Art-Pistole: dunkle Konturen, Metallschimmer,
            // ausgearbeiteter Schlitten, Lauf, Visier, Abzug und Griff.
            int cx = W / 2 + 18;
            int pistolLift = (int)Math.Round(jumpHeight * 0.62);
            Color outline = Color.FromRgb(12, 14, 18);
            Color metalDark = Color.FromRgb(36, 42, 49);
            Color metal = Color.FromRgb(82, 93, 103);
            Color metalLight = Color.FromRgb(157, 171, 180);
            Color metalGlint = Color.FromRgb(211, 222, 226);
            Color grip = Color.FromRgb(63, 39, 28);
            Color gripLight = Color.FromRgb(125, 76, 47);

            if (powerUpTicks > 0)
            {
                // Dezentes Leuchten der Waffe während des Power-ups.
                FillRect(cx - 28, H - 91 - pistolLift, 58, 8, Color.FromRgb(30, 92, 80));
                FillRect(cx - 24, H - 87 - pistolLift, 50, 4, Color.FromRgb(45, 166, 129));
            }

            // Hand und Griff mit mehreren Schattierungen.
            FillRect(cx - 1, H - 40 - pistolLift, 36, 40, Color.FromRgb(43, 31, 27));
            FillRect(cx + 2, H - 34 - pistolLift, 29, 34, grip);
            FillRect(cx + 5, H - 32 - pistolLift, 4, 25, gripLight);
            FillRect(cx + 12, H - 31 - pistolLift, 3, 25, Color.FromRgb(37, 25, 21));
            FillRect(cx + 19, H - 30 - pistolLift, 4, 24, gripLight);
            FillRect(cx + 26, H - 29 - pistolLift, 3, 22, Color.FromRgb(42, 28, 22));
            FillRect(cx + 1, H - 10 - pistolLift, 30, 5, outline);

            // Unterer Rahmen und Abzugsbügel.
            FillRect(cx - 22, H - 61 - pistolLift, 49, 35, outline);
            FillRect(cx - 18, H - 59 - pistolLift, 41, 30, metalDark);
            FillRect(cx - 12, H - 55 - pistolLift, 28, 22, metal);
            FillRect(cx - 8, H - 52 - pistolLift, 21, 17, metalDark);
            FillRect(cx - 5, H - 48 - pistolLift, 15, 10, outline);
            FillRect(cx - 2, H - 46 - pistolLift, 10, 7, Color.FromRgb(170, 143, 82));
            FillRect(cx + 1, H - 46 - pistolLift, 3, 7, Color.FromRgb(219, 195, 128));

            // Schlitten mit klaren Kanten und Lichtreflexionen.
            FillRect(cx - 23, H - 88 - pistolLift, 48, 34, outline);
            FillRect(cx - 20, H - 86 - pistolLift, 42, 30, metalDark);
            FillRect(cx - 17, H - 84 - pistolLift, 36, 25, metal);
            FillRect(cx - 15, H - 83 - pistolLift, 30, 3, metalGlint);
            FillRect(cx - 13, H - 79 - pistolLift, 25, 3, metalLight);
            FillRect(cx - 16, H - 74 - pistolLift, 31, 2, metalDark);
            FillRect(cx - 15, H - 71 - pistolLift, 28, 2, metalLight);
            FillRect(cx - 14, H - 67 - pistolLift, 26, 3, metalDark);
            FillRect(cx - 11, H - 65 - pistolLift, 22, 2, metalLight);

            // Lauf und dunkle Mündungsöffnung.
            FillRect(cx - 10, H - 96 - pistolLift, 25, 15, outline);
            FillRect(cx - 8, H - 94 - pistolLift, 21, 12, metalDark);
            FillRect(cx - 6, H - 93 - pistolLift, 16, 8, metalLight);
            FillRect(cx - 4, H - 92 - pistolLift, 12, 6, outline);
            FillRect(cx - 2, H - 91 - pistolLift, 8, 4, Color.FromRgb(3, 4, 6));
            FillRect(cx - 1, H - 90 - pistolLift, 4, 2, Color.FromRgb(40, 47, 55));

            // Visier, Schlittenrillen und kleine Befestigungsschrauben.
            FillRect(cx - 4, H - 100 - pistolLift, 6, 5, outline);
            FillRect(cx - 3, H - 99 - pistolLift, 4, 3, metalGlint);
            for (int x = cx - 16; x < cx + 14; x += 4)
                FillRect(x, H - 62 - pistolLift, 2, 5, outline);
            SetPixel(cx - 16, H - 69 - pistolLift, metalGlint);
            SetPixel(cx + 14, H - 69 - pistolLift, metalGlint);
            FillRect(cx + 17, H - 57 - pistolLift, 4, 6, Color.FromRgb(154, 35, 31));
            FillRect(cx + 18, H - 56 - pistolLift, 2, 4, Color.FromRgb(235, 69, 44));

            // Kleiner Power-up-Akzent auf dem Waffenschlitten.
            if (powerUpTicks > 0)
            {
                FillRect(cx - 3, H - 78 - pistolLift, 8, 3, Color.FromRgb(75, 255, 183));
                SetPixel(cx + 1, H - 79 - pistolLift, Color.FromRgb(220, 255, 230));
            }
        }


        void DrawHeavyWeapon()
        {
            // Breitere schwere Sturmkanone mit Doppel-Lauf, Kühlschlitzen,
            // verstärktem Gehäuse und beiden Händen. Sie folgt der Sprungbewegung.
            int cx = W / 2 + 15;
            int lift = (int)Math.Round(jumpHeight * 0.62);
            Color outline = Color.FromRgb(10, 12, 15);
            Color dark = Color.FromRgb(28, 34, 39);
            Color steel = Color.FromRgb(74, 86, 93);
            Color light = Color.FromRgb(154, 171, 176);
            Color shine = Color.FromRgb(207, 221, 218);
            Color grip = Color.FromRgb(74, 43, 29);

            // Grüne Energieleiste signalisiert die freigeschaltete schwere Waffe.
            FillRect(cx - 33, H - 117 - lift, 66, 7,
                powerUpTicks > 0 ? Color.FromRgb(45, 220, 150) : Color.FromRgb(55, 110, 105));
            FillRect(cx - 28, H - 114 - lift, 56, 2, Color.FromRgb(154, 240, 207));

            // Hintere Hand und Griff.
            FillRect(cx + 14, H - 46 - lift, 37, 46, Color.FromRgb(38, 29, 26));
            FillRect(cx + 19, H - 37 - lift, 27, 37, grip);
            FillRect(cx + 23, H - 33 - lift, 4, 25, Color.FromRgb(134, 79, 45));
            FillRect(cx + 31, H - 32 - lift, 4, 24, Color.FromRgb(47, 28, 22));
            FillRect(cx + 39, H - 30 - lift, 3, 22, Color.FromRgb(126, 72, 42));

            // Vorderer Unterarm/Hand.
            FillRect(cx - 35, H - 43 - lift, 35, 43, Color.FromRgb(71, 47, 35));
            FillRect(cx - 31, H - 35 - lift, 27, 31, Color.FromRgb(104, 70, 48));
            FillRect(cx - 28, H - 31 - lift, 4, 23, Color.FromRgb(154, 104, 68));

            // Hauptgehäuse und Schulterstück.
            FillRect(cx - 37, H - 91 - lift, 78, 50, outline);
            FillRect(cx - 33, H - 88 - lift, 70, 43, dark);
            FillRect(cx - 29, H - 85 - lift, 62, 35, steel);
            FillRect(cx - 27, H - 84 - lift, 56, 5, light);
            FillRect(cx - 24, H - 78 - lift, 48, 3, shine);
            FillRect(cx - 30, H - 72 - lift, 63, 4, dark);
            FillRect(cx - 27, H - 67 - lift, 56, 5, steel);
            FillRect(cx - 24, H - 65 - lift, 48, 2, light);

            // Zwei dicke Läufe mit schwarzen Mündungen.
            FillRect(cx - 30, H - 109 - lift, 24, 24, outline);
            FillRect(cx + 2, H - 109 - lift, 24, 24, outline);
            FillRect(cx - 27, H - 106 - lift, 18, 20, dark);
            FillRect(cx + 5, H - 106 - lift, 18, 20, dark);
            FillRect(cx - 24, H - 104 - lift, 12, 16, light);
            FillRect(cx + 8, H - 104 - lift, 12, 16, light);
            FillRect(cx - 22, H - 102 - lift, 8, 12, outline);
            FillRect(cx + 10, H - 102 - lift, 8, 12, outline);
            FillRect(cx - 20, H - 100 - lift, 4, 8, Color.FromRgb(2, 3, 4));
            FillRect(cx + 12, H - 100 - lift, 4, 8, Color.FromRgb(2, 3, 4));
            FillRect(cx - 25, H - 106 - lift, 10, 2, shine);
            FillRect(cx + 7, H - 106 - lift, 10, 2, shine);

            // Laufkühlung, Visier und Warnmarkierung.
            for (int x = cx - 20; x <= cx + 22; x += 6)
                FillRect(x, H - 60 - lift, 3, 8, outline);
            FillRect(cx - 4, H - 96 - lift, 9, 7, outline);
            FillRect(cx - 2, H - 94 - lift, 5, 3, shine);
            FillRect(cx + 24, H - 76 - lift, 7, 9, Color.FromRgb(156, 34, 25));
            FillRect(cx + 26, H - 74 - lift, 3, 5, Color.FromRgb(242, 73, 46));
            FillRect(cx - 13, H - 54 - lift, 20, 10, outline);
            FillRect(cx - 10, H - 52 - lift, 14, 6, Color.FromRgb(202, 164, 86));

            // Power-up gibt der schweren Waffe einen hellen grünen Laufakzent.
            if (powerUpTicks > 0)
            {
                FillRect(cx - 27, H - 89 - lift, 55, 3, Color.FromRgb(80, 255, 181));
                SetPixel(cx - 18, H - 91 - lift, Color.FromRgb(235, 255, 227));
                SetPixel(cx + 15, H - 91 - lift, Color.FromRgb(235, 255, 227));
            }
        }


        // Klassische Doom-artige Statusleiste am unteren Bildschirmrand.
        // Sie wird direkt in dieselbe WriteableBitmap gezeichnet und benötigt
        // deshalb keine zusätzlichen XAML-Steuerelemente.
        void DrawHudBar()
        {
            const int barHeight = 43;
            int top = H - barHeight;

            // Dunkles, metallisches Panel mit mehreren Schattierungsstreifen.
            for (int y = 0; y < barHeight; y++)
            {
                Color rowColor;
                if (y == 0) rowColor = Color.FromRgb(180, 178, 169);
                else if (y == 1) rowColor = Color.FromRgb(94, 94, 91);
                else if (y < 5) rowColor = Color.FromRgb(67, 67, 65);
                else if (y > barHeight - 3) rowColor = Color.FromRgb(20, 21, 22);
                else rowColor = (y % 4 == 0)
                    ? Color.FromRgb(49, 50, 50)
                    : Color.FromRgb(39, 40, 40);
                FillRect(0, top + y, W, 1, rowColor);
            }

            // Kleine genietete/geriffelte Details im Metallpanel.
            for (int x = 4; x < W; x += 16)
            {
                SetPixel(x, top + 3, Color.FromRgb(120, 119, 111));
                SetPixel(x + 1, top + 3, Color.FromRgb(25, 26, 27));
            }

            // Segment-Trennungen: Munition, Leben, Waffen, Gesicht, Rüstung, Reserve.
            int[] separators = { 53, 118, 160, 203, 259 };
            foreach (int x in separators)
            {
                FillRect(x, top + 2, 2, barHeight - 4, Color.FromRgb(19, 20, 21));
                SetPixel(x + 2, top + 3, Color.FromRgb(105, 104, 98));
            }

            Color brightText = Color.FromRgb(220, 220, 207);
            Color redText = Color.FromRgb(235, 43, 43);
            Color yellowText = Color.FromRgb(240, 210, 75);
            Color blueText = Color.FromRgb(111, 172, 225);

            // AMMO – Munition der aktuell gewählten Waffe.
            DrawHudText(Math.Clamp(CurrentWeaponAmmo(), 0, 999).ToString("000"), 7, top + 5, redText, 2);
            DrawHudText(CurrentWeaponAmmo() <= 0 ? "MELEE" : "AMMO", 8, top + 29,
                CurrentWeaponAmmo() <= 0 ? yellowText : brightText, 1);

            // HEALTH – Lebenspunkte als Prozentwert.
            int healthValue = Math.Clamp((int)Math.Round(health), 0, 100);
            DrawHudText(healthValue.ToString("000") + "%", 59, top + 5,
                healthValue <= 25 ? Color.FromRgb(255, 45, 35) : redText, 2);
            DrawHudText("HEALTH", 61, top + 29, brightText, 1);

            // ARMS – 1 ist die Pistole; 2 wird ab Level 13 freigeschaltet.
            DrawHudText("ARMS", 124, top + 5, brightText, 1);
            for (int slot = 0; slot < 4; slot++)
            {
                int sx = 121 + slot * 9;
                bool unlocked = slot == 0 || (slot == 1 && heavyWeaponUnlocked && currentLevel >= 13);
                bool selected = slot == selectedWeapon - 1 && unlocked;
                FillRect(sx, top + 17, 8, 15,
                    selected ? Color.FromRgb(99, 71, 45) : Color.FromRgb(35, 36, 36));
                FillRect(sx, top + 17, 8, 1,
                    selected ? Color.FromRgb(239, 188, 91) : Color.FromRgb(91, 91, 85));
                DrawHudText((slot + 1).ToString(), sx + 2, top + 22,
                    selected ? yellowText : (unlocked ? Color.FromRgb(170, 170, 158) : Color.FromRgb(72, 72, 68)), 1);
            }

            // Charakterporträt in der Mitte, mit Helm, Haut, Augen und Mund.
            DrawHudFace(164, top + 3);

            // ARMOR – Rüstungspunkte, die einen Teil des Kontaktschadens abfangen.
            int armorValue = Math.Clamp((int)Math.Round(armor), 0, 100);
            DrawHudText(armorValue.ToString("000") + "%", 209, top + 5,
                armorValue <= 20 ? Color.FromRgb(245, 75, 45) : redText, 2);
            DrawHudText("ARMOR", 216, top + 29, brightText, 1);

            // Munition beider Waffen: BULL = Pistole, RIFL = schwere Sturmkanone.
            DrawHudText("BULL", 265, top + 6, blueText, 1);
            DrawHudText(Math.Clamp(ammo, 0, 999).ToString("000"), 286, top + 6, yellowText, 1);
            DrawHudText(heavyWeaponUnlocked && currentLevel >= 13 ? "RIFL" : "LOCK", 265, top + 22, blueText, 1);
            DrawHudText(heavyWeaponUnlocked && currentLevel >= 13
                ? Math.Clamp(heavyAmmo, 0, 999).ToString("000") : "---",
                286, top + 22, heavyWeaponUnlocked && currentLevel >= 13 ? yellowText : brightText, 1);
            DrawHudText("ROKT", 331, top + 6, blueText, 1);
            DrawHudText("000", 352, top + 6, brightText, 1);
            DrawHudText("CELL", 331, top + 22, blueText, 1);
            DrawHudText("000", 352, top + 22, brightText, 1);

            // Winzige rote Anzeigenpunkte rechts unten wie auf der Referenz-HUD.
            FillRect(385, top + 6, 4, 4, Color.FromRgb(160, 30, 27));
            FillRect(385, top + 22, 4, 4, Color.FromRgb(160, 30, 27));
            SetPixel(386, top + 7, Color.FromRgb(255, 100, 65));
            SetPixel(386, top + 23, Color.FromRgb(255, 100, 65));
        }

        // Pixel-Schrift mit 3x5-Glyphen, damit der HUD-Text schnell und scharf bleibt.
        void DrawHudText(string text, int x, int y, Color color, int scale = 1)
        {
            int cursorX = x;
            foreach (char raw in text)
            {
                string[] glyph = GetHudGlyph(char.ToUpperInvariant(raw));
                for (int gy = 0; gy < glyph.Length; gy++)
                {
                    for (int gx = 0; gx < glyph[gy].Length; gx++)
                    {
                        if (glyph[gy][gx] == '1')
                            FillRect(cursorX + gx * scale, y + gy * scale,
                                scale, scale, color);
                    }
                }
                cursorX += 4 * scale;
            }
        }

        string[] GetHudGlyph(char c)
        {
            return c switch
            {
                '0' => new[] { "111", "101", "101", "101", "111" },
                '1' => new[] { "010", "110", "010", "010", "111" },
                '2' => new[] { "110", "001", "010", "100", "111" },
                '3' => new[] { "110", "001", "010", "001", "110" },
                '4' => new[] { "101", "101", "111", "001", "001" },
                '5' => new[] { "111", "100", "110", "001", "110" },
                '6' => new[] { "011", "100", "110", "101", "111" },
                '7' => new[] { "111", "001", "010", "010", "010" },
                '8' => new[] { "111", "101", "111", "101", "111" },
                '9' => new[] { "111", "101", "111", "001", "110" },
                'A' => new[] { "010", "101", "111", "101", "101" },
                'B' => new[] { "110", "101", "110", "101", "110" },
                'C' => new[] { "111", "100", "100", "100", "111" },
                'D' => new[] { "110", "101", "101", "101", "110" },
                'E' => new[] { "111", "100", "110", "100", "111" },
                'F' => new[] { "111", "100", "110", "100", "100" },
                'G' => new[] { "111", "100", "101", "101", "111" },
                'H' => new[] { "101", "101", "111", "101", "101" },
                'I' => new[] { "111", "010", "010", "010", "111" },
                'J' => new[] { "001", "001", "001", "101", "111" },
                'K' => new[] { "101", "101", "110", "101", "101" },
                'L' => new[] { "100", "100", "100", "100", "111" },
                'M' => new[] { "101", "111", "111", "101", "101" },
                'N' => new[] { "101", "111", "111", "111", "101" },
                'O' => new[] { "111", "101", "101", "101", "111" },
                'P' => new[] { "110", "101", "110", "100", "100" },
                'Q' => new[] { "111", "101", "101", "111", "001" },
                'R' => new[] { "110", "101", "110", "101", "101" },
                'S' => new[] { "111", "100", "111", "001", "110" },
                'T' => new[] { "111", "010", "010", "010", "010" },
                'U' => new[] { "101", "101", "101", "101", "111" },
                'V' => new[] { "101", "101", "101", "101", "010" },
                'W' => new[] { "101", "101", "111", "111", "101" },
                'X' => new[] { "101", "101", "010", "101", "101" },
                'Y' => new[] { "101", "101", "010", "010", "010" },
                'Z' => new[] { "111", "001", "010", "100", "111" },
                '%' => new[] { "101", "001", "010", "100", "101" },
                '-' => new[] { "000", "000", "111", "000", "000" },
                ' ' => new[] { "000", "000", "000", "000", "000" },
                _ => new[] { "000", "000", "000", "000", "000" }
            };
        }

        void DrawHudFace(int x, int y)
        {
            // Eigenes kleines Space-Marine-Porträt, im Pixel-Stil der Vorlage.
            FillRect(x - 2, y, 38, 38, Color.FromRgb(14, 15, 16));
            FillRect(x, y + 2, 34, 34, Color.FromRgb(68, 72, 67));
            FillRect(x + 3, y + 3, 28, 8, Color.FromRgb(22, 31, 29)); // Helm
            FillRect(x + 5, y + 7, 24, 20, Color.FromRgb(190, 133, 87)); // Gesicht
            FillRect(x + 5, y + 7, 24, 4, Color.FromRgb(55, 66, 59));
            FillRect(x + 4, y + 10, 5, 14, Color.FromRgb(45, 54, 49));
            FillRect(x + 25, y + 10, 5, 14, Color.FromRgb(45, 54, 49));
            FillRect(x + 9, y + 13, 5, 3, Color.FromRgb(244, 225, 174));
            FillRect(x + 21, y + 13, 5, 3, Color.FromRgb(244, 225, 174));
            FillRect(x + 10, y + 14, 3, 2, Color.FromRgb(25, 28, 24));
            FillRect(x + 22, y + 14, 3, 2, Color.FromRgb(25, 28, 24));
            FillRect(x + 16, y + 15, 3, 6, Color.FromRgb(133, 83, 55)); // Nase
            FillRect(x + 11, y + 22, 14, 3, Color.FromRgb(77, 32, 28)); // Mund
            FillRect(x + 13, y + 22, 10, 1, Color.FromRgb(231, 191, 143));
            FillRect(x + 3, y + 26, 28, 7, Color.FromRgb(34, 42, 39)); // Hals/Armor
            FillRect(x + 7, y + 29, 20, 3, Color.FromRgb(130, 42, 31));
            FillRect(x + 8, y + 4, 18, 2, Color.FromRgb(139, 156, 137));
            FillRect(x + 15, y + 3, 5, 2, Color.FromRgb(192, 204, 177));

            // Niedrige Lebensenergie lässt das Gesicht leicht röter wirken.
            if (health <= 25)
            {
                FillRect(x + 5, y + 17, 4, 5, Color.FromRgb(177, 55, 46));
                FillRect(x + 25, y + 17, 4, 5, Color.FromRgb(177, 55, 46));
            }
        }

        bool SupplyBoxPathClear(double x1, double y1, double x2, double y2)
        {
            double distance = Math.Sqrt(
                (x2 - x1) * (x2 - x1) +
                (y2 - y1) * (y2 - y1));
            int steps = Math.Max(1, (int)(distance * 20));

            for (int i = 1; i < steps; i++)
            {
                double t = (double)i / steps;
                double x = x1 + (x2 - x1) * t;
                double y = y1 + (y2 - y1) * t;
                int mx = (int)x;
                int my = (int)y;

                if (my < 0 || my >= map.Length ||
                    mx < 0 || mx >= map[my].Length ||
                    map[my][mx] == '1' || map[my][mx] == '2')
                    return false;
            }

            return true;
        }

        void SpawnSupplyBox()
        {
            supplyAvailable = false;
            supplyMessage = "";
            supplyMessageTicks = 0;

            double bestScore = double.NegativeInfinity;
            double bestX = -1;
            double bestY = -1;

            // Suche ein freies, möglichst gut erreichbares Feld.
            for (int y = 1; y < map.Length - 1; y++)
            {
                for (int x = 1; x < map[y].Length - 1; x++)
                {
                    if (map[y][x] != '0')
                        continue;

                    double candidateX = x + 0.5;
                    double candidateY = y + 0.5;

                    if (!IsValidSpawn(candidateX, candidateY))
                        continue;

                    double playerDx = candidateX - px;
                    double playerDy = candidateY - py;
                    double playerDistance = Math.Sqrt(
                        playerDx * playerDx + playerDy * playerDy);

                    // Nicht direkt neben dem Spieler erscheinen.
                    if (playerDistance < 2.0)
                        continue;

                    // Bevorzuge einen Platz, der nicht direkt neben Gegnern liegt.
                    double nearestEnemy = 99.0;
                    foreach (var enemy in enemies.Where(e => e.Alive))
                    {
                        double ex = candidateX - enemy.X;
                        double ey = candidateY - enemy.Y;
                        nearestEnemy = Math.Min(nearestEnemy,
                            Math.Sqrt(ex * ex + ey * ey));
                    }

                    if (nearestEnemy < 1.25)
                        continue;

                    // Bevorzuge einen tatsächlich sichtbaren Platz. Aufgänge gelten
                    // wie Wände, weil sie in der Raycasting-Ansicht ebenfalls blockieren.
                    bool visible = SupplyBoxPathClear(px, py, candidateX, candidateY);
                    if (!visible)
                        continue;

                    double score = playerDistance + Math.Min(nearestEnemy, 5.0) * 0.35;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = candidateX;
                        bestY = candidateY;
                    }
                }
            }

            // Sicherer Fallback, falls es auf einer Karte keinen freien Kandidaten
            // mit allen Abstandsbedingungen gibt.
            if (bestX < 0)
            {
                for (int y = 1; y < map.Length - 1 && bestX < 0; y++)
                {
                    for (int x = 1; x < map[y].Length - 1; x++)
                    {
                        if (map[y][x] == '0')
                        {
                            double candidateX = x + 0.5;
                            double candidateY = y + 0.5;
                            double pdx = candidateX - px;
                            double pdy = candidateY - py;
                            if (IsValidSpawn(candidateX, candidateY) &&
                                Math.Sqrt(pdx * pdx + pdy * pdy) >= 1.5)
                            {
                                bestX = candidateX;
                                bestY = candidateY;
                                break;
                            }
                        }
                    }
                }
            }

            if (bestX >= 0)
            {
                supplyX = bestX;
                supplyY = bestY;
                supplyAvailable = true;
            }

            // Ab Level 8 kommt in etwa 45 % der Level eine zweite Kiste dazu.
            if (currentLevel >= 8 && supplyAvailable && random.NextDouble() < 0.45)
                SpawnSecondSupplyBox();
        }

        void SpawnSecondSupplyBox()
        {
            double bestScore = double.NegativeInfinity;
            double bestX = -1;
            double bestY = -1;

            for (int y = 1; y < map.Length - 1; y++)
            {
                for (int x = 1; x < map[y].Length - 1; x++)
                {
                    if (map[y][x] != '0')
                        continue;

                    double cx = x + 0.5;
                    double cy = y + 0.5;
                    if (!IsValidSpawn(cx, cy))
                        continue;

                    double playerDistance = Math.Sqrt((cx - px) * (cx - px) + (cy - py) * (cy - py));
                    double firstDistance = Math.Sqrt((cx - supplyX) * (cx - supplyX) + (cy - supplyY) * (cy - supplyY));
                    if (playerDistance < 2.0 || firstDistance < 2.2)
                        continue;

                    double nearestEnemy = 99;
                    foreach (var enemy in enemies.Where(e => e.Alive))
                    {
                        double ex = cx - enemy.X;
                        double ey = cy - enemy.Y;
                        nearestEnemy = Math.Min(nearestEnemy, Math.Sqrt(ex * ex + ey * ey));
                    }
                    if (nearestEnemy < 1.25 || !SupplyBoxPathClear(px, py, cx, cy))
                        continue;

                    double score = playerDistance + Math.Min(nearestEnemy, 5.0) * 0.35;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = cx;
                        bestY = cy;
                    }
                }
            }

            if (bestX >= 0)
            {
                secondSupplyX = bestX;
                secondSupplyY = bestY;
                secondSupplyAvailable = true;
            }
        }

        void SpawnTraps()
        {
            traps.Clear();

            // Lavarisse gibt es jetzt in JEDEM Level (auch in beiden Ebenen von Level 3
            // und in den Bossarenen). Mit steigendem Level kommen mehr dazu (max. 3).
            // Stachelfallen folgen weiterhin erst ab Level 4.
            int lavaTrapCount = Math.Min(3, 1 + (currentLevel - 1) / 6);
            int spikeCount = currentLevel >= 4 ? Math.Min(3, 1 + (currentLevel - 4) / 6) : 0;

            for (int i = 0; i < lavaTrapCount; i++)
            {
                Point? point = FindTrapLocation(TrapKind.LavaCrack);
                if (point.HasValue)
                    traps.Add(new Trap(point.Value.X, point.Value.Y, TrapKind.LavaCrack));
            }

            for (int i = 0; i < spikeCount; i++)
            {
                Point? point = FindTrapLocation(TrapKind.FloorSpikes);
                if (point.HasValue)
                    traps.Add(new Trap(point.Value.X, point.Value.Y, TrapKind.FloorSpikes));
            }
        }

        Point? FindTrapLocation(TrapKind kind)
        {
            double bestScore = double.NegativeInfinity;
            Point? best = null;

            for (int y = 1; y < map.Length - 1; y++)
            {
                for (int x = 1; x < map[y].Length - 1; x++)
                {
                    if (map[y][x] != '0') continue;
                    double cx = x + 0.5;
                    double cy = y + 0.5;
                    if (!IsValidSpawn(cx, cy)) continue;

                    double playerDistance = Distance(cx, cy, px, py);
                    if (playerDistance < 2.0) continue;
                    if (traps.Any(t => Distance(t.X, t.Y, cx, cy) < 1.8)) continue;
                    if (supplyAvailable && Distance(supplyX, supplyY, cx, cy) < 1.0) continue;
                    if (secondSupplyAvailable && Distance(secondSupplyX, secondSupplyY, cx, cy) < 1.0) continue;
                    if (powerUpAvailable && Distance(powerUpX, powerUpY, cx, cy) < 1.0) continue;

                    double nearestEnemy = 99;
                    foreach (var enemy in enemies.Where(e => e.Alive))
                        nearestEnemy = Math.Min(nearestEnemy, Distance(cx, cy, enemy.X, enemy.Y));
                    if (nearestEnemy < 1.1 || !SupplyBoxPathClear(px, py, cx, cy)) continue;

                    double score = Math.Min(playerDistance, 8.0) + Math.Min(nearestEnemy, 4.0) * 0.35 + random.NextDouble() * 0.15;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new Point(cx, cy);
                    }
                }
            }
            return best;
        }

        int GetViewHorizon()
        {
            // Beim Springen wandert der Horizont nach unten, als würde die Kamera steigen.
            return Math.Clamp(H / 2 + (int)Math.Round(jumpHeight), 70, H - 65);
        }

        void UpdateTrapInteractions()
        {
            foreach (var trap in traps)
            {
                if (trap.CooldownTicks > 0) trap.CooldownTicks--;
                if (trap.ActiveTicks > 0) trap.ActiveTicks--;
                if (trap.CooldownTicks > 0) continue;

                double distance = Distance(px, py, trap.X, trap.Y);
                bool triggered = trap.Kind switch
                {
                    TrapKind.FloorSpikes => distance < 0.48 && jumpHeight < 8.0 && dashTicks <= 0,
                    TrapKind.LavaCrack => distance < 0.56 && jumpHeight < 10.0 && dashTicks <= 0,
                    _ => false
                };
                if (!triggered) continue;

                trap.ActiveTicks = 24;
                trap.CooldownTicks = 100;
                double baseDamage = trap.Kind == TrapKind.LavaCrack ? 12.0 : 8.0;
                double damage = baseDamage + Math.Max(0, currentLevel - 4) * 0.22;
                TakeDamage(damage);
                trapMessage = trap.Kind == TrapKind.LavaCrack
                    ? "LAVARISS! Spring rechtzeitig darüber!"
                    : "BODENFALLE! Springe über die Stacheln!";
                trapMessageTicks = 120;

                if (health <= 0)
                {
                    health = 0;
                    dead = true;
                    EndGame("GAME OVER – FALLE");
                    return;
                }
            }
        }

        void TakeDamage(double incomingDamage)
        {
            if (trainingMode) return;
            double absorbedDamage = Math.Min(armor, incomingDamage / 3.0);
            armor = Math.Max(0, armor - absorbedDamage);
            health = Math.Max(0, health - (incomingDamage - absorbedDamage));
        }

        void DrawTraps()
        {
            int horizon = GetViewHorizon();
            foreach (var trap in traps.OrderByDescending(t => Distance(px, py, t.X, t.Y)))
            {
                double dx = trap.X - px;
                double dy = trap.Y - py;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 0.25) continue;

                double direction = Math.Atan2(dy, dx);
                double difference = Math.Atan2(Math.Sin(direction - angle), Math.Cos(direction - angle));
                if (Math.Abs(difference) > 0.9) continue;

                int screenX = (int)(W / 2.0 + Math.Tan(difference) * W / 1.32);
                bool active = trap.ActiveTicks > 0;

                if (trap.Kind == TrapKind.FloorSpikes)
                {
                    int size = Math.Min(48, (int)(H / distance * 0.22));
                    if (size < 3) continue;
                    int left = screenX - size / 2;
                    int bottom = (int)(horizon + H / distance * 0.46);
                    int top = bottom - Math.Max(3, size / 2);
                    string[] sprite = active
                        ? new[] { "  RRRRRR  ", " RRRRRRRR ", "RRYYRRYYRR", " RRRRRRRR ", "  RRRRRR  " }
                        : new[] { "  MMMMMM  ", " MMMMMMMM ", "MMSSSSSSMM", " MMSMSMM  ", "  MMMMMM  " };
                    DrawTrapSprite(sprite, left, top, size, distance, active, false);
                }
                else // LavaCrack: der Riss selbst wird in der Bodenebene gezeichnet (RenderFloorAndCeiling).
                {
                    DrawLavaEmbers(trap, screenX, horizon, distance, difference, active);
                }
            }
        }

        void DrawTrapSprite(string[] sprite, int left, int top, int size, double distance, bool active, bool wallTrap)
        {
            int spriteW = sprite.Max(row => row.Length);
            int spriteH = sprite.Length;
            for (int sy = 0; sy < spriteH; sy++)
            {
                int offset = (spriteW - sprite[sy].Length) / 2;
                for (int sx = 0; sx < spriteW; sx++)
                {
                    int ci = sx - offset;
                    char c = ci >= 0 && ci < sprite[sy].Length ? sprite[sy][ci] : ' ';
                    if (c == ' ') continue;
                    Color color = c switch
                    {
                        'R' => Color.FromRgb(225, 44, 35),
                        'Y' => Color.FromRgb(255, 220, 72),
                        'M' => Color.FromRgb(76, 83, 88),
                        'S' => Color.FromRgb(159, 171, 173),
                        _ => Color.FromRgb(109, 36, 31)
                    };
                    if (active && c != 'Y')
                        color = Color.FromRgb(255, (byte)Math.Min(190, color.G + 45), (byte)Math.Min(120, color.B + 35));
                    double shade = Math.Clamp(1.15 - distance * 0.065, 0.3, 1.0);
                    color = Color.FromRgb((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade));
                    int x0 = left + sx * size / spriteW;
                    int x1 = left + (sx + 1) * size / spriteW;
                    int y0 = top + sy * size / spriteH;
                    int y1 = top + (sy + 1) * size / spriteH;
                    for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                    {
                        if (distance >= depth[x]) continue;
                        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                            SetPixel(x, y, color);
                    }
                }
            }
        }

        void DrawFlatTrapSprite(string[] sprite, int left, int top, int width, int height, double distance, bool active)
        {
            int spriteW = sprite.Max(row => row.Length);
            int spriteH = sprite.Length;
            for (int sy = 0; sy < spriteH; sy++)
            {
                int offset = (spriteW - sprite[sy].Length) / 2;
                for (int sx = 0; sx < spriteW; sx++)
                {
                    int ci = sx - offset;
                    char c = ci >= 0 && ci < sprite[sy].Length ? sprite[sy][ci] : ' ';
                    if (c == ' ') continue;
                    Color color = c switch
                    {
                        'R' => Color.FromRgb(180, 24, 14),
                        'Y' => Color.FromRgb(255, 126, 25),
                        'O' => Color.FromRgb(38, 12, 10),
                        _ => Color.FromRgb(230, 65, 20)
                    };
                    if (active && c != 'O')
                        color = c == 'Y' ? Color.FromRgb(255, 237, 100) : Color.FromRgb(255, 88, 20);
                    double shade = Math.Clamp(1.12 - distance * 0.06, 0.32, 1.0);
                    color = Color.FromRgb((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade));
                    int x0 = left + sx * width / spriteW;
                    int x1 = left + (sx + 1) * width / spriteW;
                    int y0 = top + sy * height / spriteH;
                    int y1 = top + (sy + 1) * height / spriteH;
                    for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                    {
                        if (distance >= depth[x]) continue;
                        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                            SetPixel(x, y, color);
                    }
                }
            }
        }

        void SpawnPowerUp()
        {
            powerUpAvailable = false;
            powerUpMessage = "";
            powerUpMessageTicks = 0;

            if (currentLevel < 8)
                return;

            double bestScore = double.NegativeInfinity;
            double bestX = -1;
            double bestY = -1;

            for (int y = 1; y < map.Length - 1; y++)
            {
                for (int x = 1; x < map[y].Length - 1; x++)
                {
                    if (map[y][x] != '0')
                        continue;

                    double cx = x + 0.5;
                    double cy = y + 0.5;
                    if (!IsValidSpawn(cx, cy))
                        continue;

                    double pdx = cx - px;
                    double pdy = cy - py;
                    double playerDistance = Math.Sqrt(pdx * pdx + pdy * pdy);
                    if (playerDistance < 2.5)
                        continue;

                    double nearestEnemy = 99;
                    foreach (var enemy in enemies.Where(e => e.Alive))
                    {
                        double ex = cx - enemy.X;
                        double ey = cy - enemy.Y;
                        nearestEnemy = Math.Min(nearestEnemy, Math.Sqrt(ex * ex + ey * ey));
                    }
                    if (nearestEnemy < 1.5)
                        continue;

                    if (supplyAvailable)
                    {
                        double sx = cx - supplyX;
                        double sy = cy - supplyY;
                        if (Math.Sqrt(sx * sx + sy * sy) < 2.0)
                            continue;
                    }

                    if (!SupplyBoxPathClear(px, py, cx, cy))
                        continue;

                    double score = Math.Min(playerDistance, 7.0) + Math.Min(nearestEnemy, 5.0) * 0.3;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = cx;
                        bestY = cy;
                    }
                }
            }

            if (bestX >= 0)
            {
                powerUpX = bestX;
                powerUpY = bestY;
                powerUpAvailable = true;
            }
        }

        void CheckPowerUp()
        {
            if (!powerUpAvailable)
                return;

            double dx = powerUpX - px;
            double dy = powerUpY - py;
            if (Math.Sqrt(dx * dx + dy * dy) <= 0.65)
            {
                powerUpAvailable = false;
                powerUpTicks = 30 * 60; // ungefähr 30 Sekunden bei 60 FPS
                powerUpMessage = "Power-up aktiviert: doppelter Waffenschaden!";
                powerUpMessageTicks = 180;
            }
        }

        void DrawPowerUp()
        {
            if (!powerUpAvailable)
                return;

            double dx = powerUpX - px;
            double dy = powerUpY - py;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 0.3)
                return;

            double direction = Math.Atan2(dy, dx);
            double difference = Math.Atan2(
                Math.Sin(direction - angle),
                Math.Cos(direction - angle));
            if (Math.Abs(difference) > 0.9)
                return;

            int screenX = (int)(W / 2.0 + Math.Tan(difference) * W / 1.32);
            int size = Math.Min(58, (int)(H / distance * 0.24));
            if (size < 3)
                return;

            int left = screenX - size / 2;
            int bottom = (int)(GetViewHorizon() + H / distance * 0.32 + Math.Sin(frameTick * 0.08) * H / distance * 0.02);
            int top = bottom - size;
            string[] sprite =
            {
                "   C   ",
                "  CYC  ",
                " CYGYC ",
                "CYYGYYC",
                " CYGYC ",
                "  CYC  ",
                "   C   "
            };

            int spriteW = sprite[0].Length;
            int spriteH = sprite.Length;
            for (int sy = 0; sy < spriteH; sy++)
            {
                for (int sx = 0; sx < spriteW; sx++)
                {
                    char c = sprite[sy][sx];
                    if (c == ' ')
                        continue;

                    Color color = c switch
                    {
                        'C' => Color.FromRgb(25, 150, 132),
                        'Y' => Color.FromRgb(65, 255, 196),
                        'G' => Color.FromRgb(240, 255, 220),
                        _ => Colors.White
                    };

                    int x0 = left + sx * size / spriteW;
                    int x1 = left + (sx + 1) * size / spriteW;
                    int y0 = top + sy * size / spriteH;
                    int y1 = top + (sy + 1) * size / spriteH;
                    for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                    {
                        if (distance >= depth[x])
                            continue;
                        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                        {
                            int i = (y * W + x) * 4;
                            pixels[i] = color.B;
                            pixels[i + 1] = color.G;
                            pixels[i + 2] = color.R;
                            pixels[i + 3] = 255;
                        }
                    }
                }
            }
        }

        void CheckSupplyBox()
        {
            if (supplyAvailable && Distance(px, py, supplyX, supplyY) <= 0.65)
            {
                health = 100;
                ammo = PistolAmmoCapacity;
                if (heavyWeaponUnlocked)
                    heavyAmmo = HeavyWeaponAmmoCapacity;
                supplyAvailable = false;
                supplyMessage = "Kiste eingesammelt: HP und Munition voll!";
                supplyMessageTicks = 180;
            }

            if (secondSupplyAvailable && Distance(px, py, secondSupplyX, secondSupplyY) <= 0.65)
            {
                health = 100;
                ammo = PistolAmmoCapacity;
                if (heavyWeaponUnlocked)
                    heavyAmmo = HeavyWeaponAmmoCapacity;
                secondSupplyAvailable = false;
                supplyMessage = "Zweite Versorgungskiste eingesammelt! HP und Munition voll!";
                supplyMessageTicks = 180;
            }
        }

        double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        void DrawSupplyBox()
        {
            if (supplyAvailable)
                DrawSupplyBoxAt(supplyX, supplyY);
            if (secondSupplyAvailable)
                DrawSupplyBoxAt(secondSupplyX, secondSupplyY);
        }

        void DrawSupplyBoxAt(double boxX, double boxY)
        {
            double dx = boxX - px;
            double dy = boxY - py;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < 0.3)
                return;

            double direction = Math.Atan2(dy, dx);
            double difference = Math.Atan2(
                Math.Sin(direction - angle),
                Math.Cos(direction - angle));

            if (Math.Abs(difference) > 0.9)
                return;

            int screenX = (int)(W / 2.0 + Math.Tan(difference) * W / 1.32);
            int size = Math.Min(70, (int)(H / distance * 0.30));
            if (size < 3)
                return;

            // Das Objekt steht auf dem Boden und wächst bei Annäherung.
            int left = screenX - size / 2;
            int bottom = (int)(GetViewHorizon() + H / distance * 0.47);
            int top = bottom - size;

            string[] sprite =
            {
                "  BBBBBB  ",
                " BBCCCCBB ",
                "BBCCCCCCBB",
                "BCCCCGCCCB",
                "BCHHHHCCCB",
                "BCHHHHCCCB",
                "BCCCCGCCCB",
                "BBCCCCCCBB",
                " BBCCCCBB ",
                "  BBBBBB  "
            };

            int spriteW = sprite[0].Length;
            int spriteH = sprite.Length;

            for (int sy = 0; sy < spriteH; sy++)
            {
                for (int sx = 0; sx < spriteW; sx++)
                {
                    char c = sprite[sy][sx];
                    if (c == ' ')
                        continue;

                    Color color = c switch
                    {
                        'B' => Color.FromRgb(65, 32, 12),
                        'C' => Color.FromRgb(155, 88, 32),
                        'G' => Color.FromRgb(255, 205, 45),
                        'H' => Color.FromRgb(225, 245, 225),
                        _ => Colors.White
                    };

                    int x0 = left + sx * size / spriteW;
                    int x1 = left + (sx + 1) * size / spriteW;
                    int y0 = top + sy * size / spriteH;
                    int y1 = top + (sy + 1) * size / spriteH;

                    for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
                    {
                        // Box wird von Wänden verdeckt.
                        if (distance >= depth[x])
                            continue;

                        for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                        {
                            int i = (y * W + x) * 4;
                            pixels[i] = color.B;
                            pixels[i + 1] = color.G;
                            pixels[i + 2] = color.R;
                            pixels[i + 3] = 255;
                        }
                    }
                }
            }
        }

        void DrawEnemies()
        {
            // Ausgearbeitete Retro-Pixel-Sprites. Die Zeilen dürfen unterschiedlich
            // lang sein; sie werden beim Zeichnen automatisch mittig ausgerichtet.
            string[] impSprite =
            {
                "TT          TT",
                "TTT        TTT",
                " TTTTTTTTTTT ",
                "  OOOOOOOOOO  ",
                " OORRRRRRRROO ",
                "OORRRRRRRRRROO",
                "ORRYYRRRRYYRRO",
                "ORRRRRRRRRRRRO",
                " ORRRWWWWRRRRO",
                " OORRRRRRRRROO",
                "OORRCCRRCCRRROO",
                "ORRRCCCCCCRRRO",
                "ORRRRCHHCRRRRO",
                "ORRRRCCCCRRRRO",
                " OORRRRRRRRROO",
                "  OORRRRRRROO ",
                "   OORR  RROO ",
                "   OORR  RROO ",
                "   OOFF  FFOO ",
                "   FFFF  FFFF "
            };

            string[] stalkerSprite =
            {
                "P             P",
                "PP           PP",
                " PPP         PP ",
                "  OPP       PPO  ",
                "   OPPSSSSSPPO   ",
                "  OOSSSSSSSSSOO  ",
                " OOSSSSSSSSSSSOO ",
                "OSSSYYSSSSSSSSSO",
                "OSSSSSSSSSSSSSSO",
                " OOSSSWWWSSSSOO ",
                "  OOSSSCCCSSOO  ",
                "   OSSCHHCSSO   ",
                "  OOSSSCCCSSOO  ",
                " OOSSSSSSSSSSSOO ",
                " OOPSSSSSSSSSPPO ",
                "  OOSSSSSSSSOO  ",
                "   OOSS  SSO    ",
                "   OOOS  SOO    ",
                "   OOOP  POO    "
            };

            string[] bruteSprite =
            {
                "   PP      PP   ",
                "  PPPP    PPPP  ",
                "  OPPPPPPPPPO   ",
                " OOBBBBBBBBBOO  ",
                "OOBBBBBBBBBBBOO",
                "OOBBYYYYYYBBBOO",
                "OOBBBBBBBBBBBBOO",
                "OBBBBBBWWBBBBBBO",
                "OBBHHBBBBBBHHBBO",
                "OOBBBBBCCCCBBBOO",
                " OOBBBCCCCCCBBOO",
                " OOBBBBCCJJCCBBOO",
                "  OOBBBBBBBBBBOO",
                "   OOBBBBBBBBOO ",
                "   OOBBBOOOBBBOO",
                "    OOBBB  BBBOO",
                "    OOJJJ  JJJOO",
                "    OOOO    OOOO"
            };

            string[] miniBossSprite =
            {
                "G      GG      G",
                "GG    GGGG    GG",
                " GGG GGGGGG GGG ",
                "  OOGGGGGGGGOO  ",
                " OORRRRRRRRRROO ",
                "OORRRRRRRRRRRROO",
                "ORRRYYRRRRYYRRRRO",
                "ORRRRRRRRRRRRRRRO",
                " ORRRRRWWWWRRRRRO",
                " OORRRRRRRRRRROO",
                "OORRRRCCCCRRRRROO",
                "ORRRRCCCHHCCRRRRO",
                "ORRRRRCCCCRRRRRRO",
                " OORRRRRRRRRRROO",
                "  OORRRRRRRRROO ",
                "   OOORRRRRROOO ",
                "    OORR  RROO  ",
                "    OOFF  FFOO  ",
                "     FFFF  FFFF "
            };

            string[] finalBossSprite =
            {
                "GGG            GGG",
                "GGGG          GGGG",
                " GGGGG      GGGGG ",
                "  GGGGGG  GGGGGG  ",
                "   OOGGGGGGGGGOO   ",
                "  OODDDDDDDDDDDDOO ",
                " OODDRRRRRRRRRRDDOO",
                "OODDRRRRRRRRRRRRDDOO",
                "ODDRRRYYRRRRYYRRRRDDO",
                "ODDRRRRRRRRRRRRRRRDDO",
                "ODDRRRRWWWWWWRRRRRDDO",
                " ODRRRRRRRRRRRRRRRDO ",
                " ODRRRRCCCCCCRRRRRDO ",
                "OODRRCCCHHHHCCCRRRDDOO",
                "ODDRRRCCCCCCCCRRRRDDO",
                " ODDRRRRRHHRRRRRRDDO ",
                "  ODDRRRRRRRRRRRRDDO  ",
                "   OODDRRRRRRRRDDOO   ",
                "    OODDDRRRRDDDOO    ",
                "     OOORR  RROOO     ",
                "     OOORR  RROOO     ",
                "     OOOPP  PPOOO     ",
                "      OOOO  OOOO      "
            };

            int horizon = GetViewHorizon();
            foreach (var enemy in enemies
                         .Where(e => e.Alive)
                         .OrderByDescending(e =>
                             (e.X - px) * (e.X - px) +
                             (e.Y - py) * (e.Y - py)))
            {
                string[] sprite;
                if (enemy.IsBoss)
                    sprite = currentLevel == 20 ? finalBossSprite : miniBossSprite;
                else
                    sprite = enemy.Kind switch
                    {
                        EnemyKind.Stalker => stalkerSprite,
                        EnemyKind.Brute => bruteSprite,
                        _ => impSprite
                    };

                double dx = enemy.X - px;
                double dy = enemy.Y - py;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 0.3)
                    continue;

                double direction = Math.Atan2(dy, dx);
                double difference = Math.Atan2(
                    Math.Sin(direction - angle),
                    Math.Cos(direction - angle));
                if (Math.Abs(difference) > 0.9)
                    continue;

                int screenX = (int)(W / 2.0 + Math.Tan(difference) * W / 1.32);
                double spriteScale = enemy.IsBoss
                    ? (currentLevel == 20 ? 2.05 : 1.52)
                    : (enemy.Kind == EnemyKind.Brute ? 1.24 : enemy.Kind == EnemyKind.Stalker ? 1.08 : 1.06);
                int size = Math.Min(H, (int)(H / distance * spriteScale));
                if (size < 4)
                    continue;

                int spriteW = sprite.Max(row => row.Length);
                int spriteH = sprite.Length;
                int left = screenX - size / 2;
                int baseTop = horizon - size / 2;

                // Lauf- und Atemanimation: Gegner wippen beim Verfolgen und atmen im Stand.
                double detection = enemy.IsBoss ? 4.5 : 3.0;
                double stopRange = enemy.IsBoss ? 0.85 : 0.6;
                bool chasing = distance < detection && distance > stopRange;
                double walkPhase = frameTick * (0.20 + enemy.Speed * 2.0) + enemy.Phase;
                double bobAmp = size * (chasing ? 0.035 : 0.012);
                double bobWave = chasing
                    ? Math.Abs(Math.Sin(walkPhase))
                    : 0.5 + 0.5 * Math.Sin(frameTick * 0.05 + enemy.Phase);
                int top = baseTop - (int)Math.Round(bobWave * bobAmp);

                // Weiches Energiefeld: Bosse stark und pulsierend, normale Gegner dezent.
                Color glowColor;
                double glowStrength;
                int glowRadius;
                if (enemy.IsBoss)
                {
                    glowColor = currentLevel == 20 ? Color.FromRgb(255, 74, 24) : Color.FromRgb(177, 48, 235);
                    glowStrength = 0.50 + 0.15 * Math.Sin(frameTick * 0.08);
                    glowRadius = Math.Min(110, (int)(size * 0.62));
                }
                else
                {
                    glowColor = enemy.Kind switch
                    {
                        EnemyKind.Stalker => Color.FromRgb(40, 220, 230),
                        EnemyKind.Brute => Color.FromRgb(200, 190, 60),
                        _ => Color.FromRgb(255, 90, 30)
                    };
                    glowStrength = 0.16;
                    glowRadius = Math.Min(60, (int)(size * 0.55));
                }
                if (glowRadius >= 4)
                    AddGlow(screenX, horizon, glowRadius, glowColor, glowStrength, distance);

                // Weicher Bodenschatten (wird von Wänden verdeckt).
                int shadowCy = Math.Min(H - 1, baseTop + (int)(size * 0.93));
                int shadowRx = Math.Max(2, (int)(size * (enemy.IsBoss ? 0.30 : 0.25)));
                int shadowRy = Math.Max(1, size / 26);
                for (int sx = -shadowRx; sx <= shadowRx; sx++)
                {
                    for (int sy = -shadowRy; sy <= shadowRy; sy++)
                    {
                        double ellipse = (double)(sx * sx) / (shadowRx * shadowRx)
                                       + (double)(sy * sy) / (shadowRy * shadowRy);
                        int x = screenX + sx;
                        int y = shadowCy + sy;
                        if (ellipse > 1.0 || x < 0 || x >= W || y < 0 || y >= H || distance >= depth[x])
                            continue;
                        double f = 1.0 - 0.65 * (1.0 - ellipse);
                        int si = (y * W + x) * 4;
                        pixels[si] = (byte)(pixels[si] * f);
                        pixels[si + 1] = (byte)(pixels[si + 1] * f);
                        pixels[si + 2] = (byte)(pixels[si + 2] * f);
                    }
                }

                int stepSign = Math.Sin(walkPhase) >= 0 ? 1 : -1;
                int enemySeed = (int)(enemy.Phase * 1000);
                double damageFrac = 1.0 - Math.Clamp((double)enemy.HP / enemy.MaxHP, 0, 1);
                double flash = enemy.HitFlashTicks > 0 ? enemy.HitFlashTicks / 6.0 : 0;
                double eyePulse = 0.85 + 0.15 * Math.Sin(frameTick * 0.2 + enemy.Phase);
                double distShade = Math.Clamp(1.18 - distance * 0.065, 0.28, 1.0);

                // Hochaufgelöstes, geglättetes Sprite mit berechnetem Volumen (siehe GetHiSprite).
                string spriteKey = enemy.IsBoss
                    ? (currentLevel == 20 ? "final" : "mini")
                    : enemy.Kind switch
                    {
                        EnemyKind.Stalker => "stalker",
                        EnemyKind.Brute => "brute",
                        _ => "imp"
                    };
                HiSprite hi = GetHiSprite(spriteKey, sprite);
                int hiW = hi.W, hiH = hi.H;
                int footStartHi = (int)(hiH * 0.80);
                int legShift = (hiW / spriteW) * stepSign;   // 1 altes Sprite-Pixel in Zellen

                // Farbtabelle einmal pro Gegner statt pro Bildpunkt.
                var palette = new Color[91];
                var emissivePal = new bool[91];
                for (char pc = 'A'; pc <= 'Z'; pc++)
                {
                    palette[pc] = GetEnemyPixelColor(pc, enemy);
                    emissivePal[pc] = IsEmissiveChar(pc, enemy);
                }

                double rimStrength = enemy.IsBoss ? 0.60 : 0.34;
                int xStart = Math.Max(0, left), xEnd = Math.Min(W, left + size);
                int yStart = Math.Max(0, top), yEnd = Math.Min(H, top + size);

                for (int xx = xStart; xx < xEnd; xx++)
                {
                    if (distance >= depth[xx])
                        continue;

                    int hx = Math.Clamp((int)((xx - left + 0.5) * hiW / size), 0, hiW - 1);

                    // Beine öffnen und schließen sich beim Laufen.
                    int legCol = hx < hiW / 2 ? hx - legShift : hx + legShift;

                    for (int yy = yStart; yy < yEnd; yy++)
                    {
                        int hy = Math.Clamp((int)((yy - top + 0.5) * hiH / size), 0, hiH - 1);

                        int col = hx;
                        if (chasing && size >= 24 && hy >= footStartHi)
                            col = legCol;
                        if (col < 0 || col >= hiW)
                            continue;

                        int hiIdx = hy * hiW + col;
                        char c = hi.Ch[hiIdx];
                        if (c < 'A' || c > 'Z')
                            continue;

                        Color baseColor = palette[c];
                        double rr, gg, bb;

                        if (emissivePal[c])
                        {
                            // Selbstleuchtende Pixel (Augen, Energiekerne) werden nicht abgedunkelt.
                            double em = eyePulse * 1.15;
                            rr = baseColor.R * em;
                            gg = baseColor.G * em;
                            bb = baseColor.B * em;
                        }
                        else
                        {
                            double vertical = 1.10 - 0.22 * (yy - top) / (double)size;
                            double lightMul = hi.Lit[hiIdx] * vertical * hi.Mat[hiIdx] * distShade;
                            rr = baseColor.R * lightMul;
                            gg = baseColor.G * lightMul;
                            bb = baseColor.B * lightMul;

                            if (c != 'O')
                            {
                                // Glanzlicht: Hörner, Zähne, Krallen und Panzerteile glänzen stärker.
                                double specStrength = (c == 'W' || c == 'F' || c == 'T' || c == 'G') ? 0.60 : 0.22;
                                double sp = hi.Spec[hiIdx] * specStrength * distShade;
                                rr += 255 * sp;
                                gg += 238 * sp;
                                bb += 218 * sp;
                            }

                            // Gegenlicht in der Energiefarbe des Gegners an den Rändern.
                            double rimAmount = hi.Rim[hiIdx] * rimStrength * distShade;
                            rr += glowColor.R * rimAmount;
                            gg += glowColor.G * rimAmount;
                            bb += glowColor.B * rimAmount;

                            // Sichtbare Verletzungen: Blutflecken nehmen mit dem Schaden zu.
                            if (damageFrac > 0 && c != 'O' &&
                                Hash(col / 3, hy / 3, enemySeed) % 100 < damageFrac * 16)
                            {
                                double wet = 0.75 + 0.5 * hi.Lit[hiIdx];
                                rr = 125 * distShade * wet;
                                gg = 12 * distShade * wet;
                                bb = 14 * distShade * wet;
                                rr += 200 * hi.Spec[hiIdx] * 0.5;
                                gg += 150 * hi.Spec[hiIdx] * 0.5;
                                bb += 150 * hi.Spec[hiIdx] * 0.5;
                            }
                        }

                        // Treffer-Blitz.
                        if (flash > 0)
                        {
                            rr += (255 - rr) * 0.6 * flash;
                            gg += (255 - gg) * 0.6 * flash;
                            bb += (255 - bb) * 0.6 * flash;
                        }

                        // Feine Körnung gegen flache Flächen bei Nahaufnahme.
                        double grain = size >= 40
                            ? 1.0 + ((Hash(xx, yy, 7) & 15) - 7.5) / 200.0
                            : 1.0;
                        int i = (yy * W + xx) * 4;
                        pixels[i] = (byte)Math.Clamp(bb * grain, 0, 255);
                        pixels[i + 1] = (byte)Math.Clamp(gg * grain, 0, 255);
                        pixels[i + 2] = (byte)Math.Clamp(rr * grain, 0, 255);
                        pixels[i + 3] = 255;
                    }
                }

                // Leuchtende Augen strahlen leicht in die Umgebung.
                if (size >= 16)
                {
                    int blockW = Math.Max(1, size / spriteW);
                    for (int sy = 0; sy < spriteH; sy++)
                    {
                        int rowOffset = (spriteW - sprite[sy].Length) / 2;
                        for (int ci = 0; ci < sprite[sy].Length; ci++)
                        {
                            if (sprite[sy][ci] != 'Y') continue;
                            int sx = ci + rowOffset;
                            int ex = left + (sx * 2 + 1) * size / (spriteW * 2);
                            int ey = top + (sy * 2 + 1) * size / (spriteH * 2);
                            Color eyeGlow = GetEnemyPixelColor('Y', enemy);
                            AddGlow(ex, ey, Math.Clamp(blockW * 2, 2, 14), eyeGlow, 0.35, distance);
                        }
                    }
                }

                // Gerahmter Lebensbalken mit eigener Farbe je Gegnertyp.
                int desiredBarWidth = enemy.IsBoss
                    ? (currentLevel == 20 ? 146 : 104)
                    : (enemy.Kind == EnemyKind.Brute ? 62 : 52);
                int barWidth = Math.Min(size, desiredBarWidth);
                int barHeight = enemy.IsBoss ? 5 : 4;
                int barX = screenX - barWidth / 2;
                int barY = baseTop - barHeight - 4;
                if (barY >= 0)
                {
                    Color barColor = enemy.IsBoss
                        ? (currentLevel == 20 ? Color.FromRgb(255, 174, 32) : Color.FromRgb(210, 60, 235))
                        : enemy.Kind == EnemyKind.Stalker ? Color.FromRgb(50, 230, 220)
                        : enemy.Kind == EnemyKind.Brute ? Color.FromRgb(220, 190, 65)
                        : Color.FromRgb(235, 62, 48);

                    FillRect(barX - 1, barY - 1, barWidth + 2, barHeight + 2, Color.FromRgb(18, 14, 18));
                    FillRect(barX, barY, barWidth, barHeight, Color.FromRgb(66, 25, 29));
                    int filledWidth = (int)Math.Round(barWidth * Math.Clamp((double)enemy.HP / enemy.MaxHP, 0, 1));
                    if (filledWidth > 0)
                    {
                        FillRect(barX, barY, filledWidth, barHeight, barColor);
                        if (barHeight > 3)
                            FillRect(barX, barY, filledWidth, 1, Color.FromRgb(255, 235, 195));
                    }
                }
            }
        }

        // =====================================================================
        //  HOCHAUFGELÖSTE GEGNER-SPRITES
        //  Die handgezeichneten Zeichen-Sprites werden einmalig mit zwei Scale2x-Durchgängen
        //  geglättet (je Achse 4x => 16x so viele Bildpunkte, runde Konturen statt Treppen)
        //  und bekommen ein berechnetes Volumen: Kuppel-Normalen, Licht von links oben,
        //  Glanzpunkte, Rand-Gegenlicht und eine feine Materialstruktur.
        // =====================================================================
        class HiSprite
        {
            public int W, H;
            public char[] Ch;      // Zeichenraster in hoher Auflösung (' ' = transparent)
            public float[] Lit;    // Diffuses Licht (vorberechnet)
            public float[] Spec;   // Glanzlicht
            public float[] Rim;    // Gegenlicht am unteren/rechten Rand
            public float[] Mat;    // Materialstruktur (Multiplikator um 1.0)
        }

        readonly Dictionary<string, HiSprite> hiSprites = new();

        static char[] UpscaleEpx(char[] src, int w, int h)
        {
            int nw = w * 2;
            var dst = new char[nw * h * 2];

            char At(int x, int y) => (x < 0 || y < 0 || x >= w || y >= h) ? ' ' : src[y * w + x];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    char p = At(x, y);
                    char a = At(x, y - 1), b = At(x + 1, y), c = At(x - 1, y), d = At(x, y + 1);
                    char e0 = p, e1 = p, e2 = p, e3 = p;
                    if (c == a && c != d && a != b) e0 = a;
                    if (a == b && a != c && b != d) e1 = b;
                    if (d == c && d != b && c != a) e2 = c;
                    if (b == d && b != a && d != c) e3 = d;

                    int o = (y * 2) * nw + x * 2;
                    dst[o] = e0;
                    dst[o + 1] = e1;
                    dst[o + nw] = e2;
                    dst[o + nw + 1] = e3;
                }
            }
            return dst;
        }

        HiSprite GetHiSprite(string key, string[] sprite)
        {
            if (hiSprites.TryGetValue(key, out var cached))
                return cached;

            int w = sprite.Max(row => row.Length);
            int h = sprite.Length;
            var g = new char[w * h];
            for (int y = 0; y < h; y++)
            {
                int off = (w - sprite[y].Length) / 2;
                for (int x = 0; x < w; x++)
                {
                    int ci = x - off;
                    g[y * w + x] = (ci >= 0 && ci < sprite[y].Length) ? sprite[y][ci] : ' ';
                }
            }

            for (int pass = 0; pass < 2; pass++)
            {
                g = UpscaleEpx(g, w, h);
                w *= 2;
                h *= 2;
            }

            // Abstand zum Rand (in Zellen) => Kuppelhöhe => Normalen.
            const int R = 10;
            var dpt = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (g[y * w + x] == ' ') continue;
                    double best = R;
                    for (int oy = -R; oy <= R; oy++)
                    {
                        int yy = y + oy;
                        for (int ox = -R; ox <= R; ox++)
                        {
                            int xx = x + ox;
                            bool empty = xx < 0 || yy < 0 || xx >= w || yy >= h || g[yy * w + xx] == ' ';
                            if (!empty) continue;
                            double dist = Math.Sqrt(ox * ox + oy * oy);
                            if (dist < best) best = dist;
                        }
                    }
                    dpt[y * w + x] = (float)best;
                }
            }

            var hgt = new double[w * h];
            for (int i = 0; i < w * h; i++)
            {
                if (g[i] == ' ') continue;
                double tt = Math.Min(1.0, dpt[i] / R);
                hgt[i] = Math.Sqrt(1 - (1 - tt) * (1 - tt));
            }

            var blur = new double[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double sum = 0;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                            sum += hgt[Math.Clamp(y + oy, 0, h - 1) * w + Math.Clamp(x + ox, 0, w - 1)];
                    blur[y * w + x] = sum / 9.0;
                }
            }

            // Lichtrichtung (von links oben vorne) und Halbvektor für den Glanz.
            double lx = -0.55, ly = -0.60, lz = 0.60;
            double ln = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ln; ly /= ln; lz /= ln;
            double hvx = lx, hvy = ly, hvz = lz + 1.0;
            double hn = Math.Sqrt(hvx * hvx + hvy * hvy + hvz * hvz);
            hvx /= hn; hvy /= hn; hvz /= hn;

            var lit = new float[w * h];
            var spec = new float[w * h];
            var rim = new float[w * h];
            var mat = new float[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (g[i] == ' ') continue;

                    double gx = blur[y * w + Math.Min(x + 1, w - 1)] - blur[y * w + Math.Max(x - 1, 0)];
                    double gy = blur[Math.Min(y + 1, h - 1) * w + x] - blur[Math.Max(y - 1, 0) * w + x];
                    double nx = -gx * R * 0.55, ny = -gy * R * 0.55;
                    double nl = Math.Sqrt(nx * nx + ny * ny + 1.0);
                    nx /= nl; ny /= nl;
                    double nz = 1.0 / nl;

                    double diff = Math.Max(0, nx * lx + ny * ly + nz * lz);
                    lit[i] = (float)(0.42 + 0.78 * diff);
                    spec[i] = (float)Math.Pow(Math.Max(0, nx * hvx + ny * hvy + nz * hvz), 18);
                    rim[i] = (float)Math.Pow(Math.Max(0, nx * 0.73 + ny * 0.68), 1.6);

                    double blot = ValueNoise(x / 5.0, y / 5.0, 64, 91);
                    double cell = Noise01(x, y, 92);
                    mat[i] = (float)(0.90 + 0.14 * blot + 0.06 * cell);
                }
            }

            var result = new HiSprite { W = w, H = h, Ch = g, Lit = lit, Spec = spec, Rim = rim, Mat = mat };
            hiSprites[key] = result;
            return result;
        }

        static char SpriteCharAt(string[] sprite, int spriteW, int sx, int sy)
        {
            if (sy < 0 || sy >= sprite.Length) return ' ';
            int rowOffset = (spriteW - sprite[sy].Length) / 2;
            int ci = sx - rowOffset;
            return ci >= 0 && ci < sprite[sy].Length ? sprite[sy][ci] : ' ';
        }

        // Selbstleuchtende Sprite-Pixel (Augen, Energiekerne) werden nicht abgedunkelt.
        bool IsEmissiveChar(char c, Enemy e)
        {
            if (e.IsBoss)
                return currentLevel == 20 ? (c == 'Y' || c == 'C' || c == 'H') : (c == 'Y' || c == 'H');
            if (e.Kind == EnemyKind.Stalker)
                return c == 'Y' || c == 'H' || c == 'C';
            return c == 'Y';
        }

        Color GetEnemyPixelColor(char c, Enemy enemy)
        {
            if (enemy.IsBoss && currentLevel == 20)
            {
                return c switch
                {
                    'G' => Color.FromRgb(255, 202, 55),
                    'D' => Color.FromRgb(91, 14, 18),
                    'R' => Color.FromRgb(214, 43, 30),
                    'Y' => Color.FromRgb(255, 251, 153),
                    'W' => Color.FromRgb(255, 234, 190),
                    'C' => Color.FromRgb(255, 119, 25),
                    'H' => Color.FromRgb(255, 231, 110),
                    'P' => Color.FromRgb(50, 12, 16),
                    'O' => Color.FromRgb(25, 10, 15),
                    _ => Color.FromRgb(200, 42, 25)
                };
            }

            if (enemy.IsBoss)
            {
                return c switch
                {
                    'G' => Color.FromRgb(151, 72, 207),
                    'R' => Color.FromRgb(122, 40, 177),
                    'Y' => Color.FromRgb(255, 70, 205),
                    'W' => Color.FromRgb(252, 220, 255),
                    'C' => Color.FromRgb(84, 33, 133),
                    'H' => Color.FromRgb(246, 111, 250),
                    'F' => Color.FromRgb(202, 163, 234),
                    'O' => Color.FromRgb(37, 12, 56),
                    _ => Color.FromRgb(129, 42, 177)
                };
            }

            if (enemy.Kind == EnemyKind.Stalker)
            {
                return c switch
                {
                    'P' => Color.FromRgb(13, 73, 88),
                    'S' => Color.FromRgb(29, 157, 178),
                    'Y' => Color.FromRgb(197, 255, 107),
                    'W' => Color.FromRgb(223, 255, 250),
                    'C' => Color.FromRgb(28, 212, 208),
                    'H' => Color.FromRgb(100, 255, 220),
                    'O' => Color.FromRgb(7, 30, 43),
                    _ => Color.FromRgb(31, 145, 169)
                };
            }

            if (enemy.Kind == EnemyKind.Brute)
            {
                return c switch
                {
                    'P' => Color.FromRgb(184, 164, 81),
                    'B' => Color.FromRgb(108, 119, 61),
                    'Y' => Color.FromRgb(255, 191, 48),
                    'W' => Color.FromRgb(246, 224, 166),
                    'C' => Color.FromRgb(146, 124, 55),
                    'H' => Color.FromRgb(191, 195, 119),
                    'J' => Color.FromRgb(37, 43, 27),
                    'O' => Color.FromRgb(38, 43, 27),
                    _ => Color.FromRgb(115, 124, 65)
                };
            }

            return c switch
            {
                'T' => Color.FromRgb(224, 157, 70),
                'R' => Color.FromRgb(188, 46, 33),
                'Y' => Color.FromRgb(255, 232, 45),
                'W' => Color.FromRgb(255, 224, 191),
                'C' => Color.FromRgb(126, 36, 25),
                'H' => Color.FromRgb(255, 117, 55),
                'F' => Color.FromRgb(203, 183, 154),
                'O' => Color.FromRgb(67, 18, 18),
                _ => Color.FromRgb(178, 38, 29)
            };
        }

        void EndGame(string message)
        {
            started = false;
            isPaused = false;
            timer.Stop();
            if (soundtrackAvailable)
                soundtrackPlayer.Pause();
            if (resumeButton != null)
                resumeButton.Visibility = Visibility.Collapsed;
            if (mainMenuStartButton != null)
                mainMenuStartButton.Content = "NEU STARTEN";

            Overlay.Visibility = Visibility.Visible;

            var panel = Overlay.Child as System.Windows.Controls.StackPanel;

            if (panel != null)
            {
                var title = panel.Children[0] as System.Windows.Controls.TextBlock;

                if (title != null)
                    title.Text = message;

                var button = panel.Children[3] as System.Windows.Controls.Button;

                if (button != null)
                    button.Content = "NEU STARTEN";
            }
        }

        bool IsValidSpawn(double x, double y)
        {
            int mx = (int)x;
            int my = (int)y;

            if (my < 0 || my >= map.Length ||
                mx < 0 || mx >= map[my].Length)
                return false;

            // Weder Wände noch Aufgänge als Spawnpunkt verwenden.
            if (map[my][mx] != '0')
                return false;

            return !IsWall(x + 0.2, y) &&
                   !IsWall(x - 0.2, y) &&
                   !IsWall(x, y + 0.2) &&
                   !IsWall(x, y - 0.2);
        }

        void FixEnemySpawns()
        {
            foreach (var enemy in enemies)
            {
                if (IsValidSpawn(enemy.X, enemy.Y))
                    continue;

                // Suche ein freies Feld in der Karte.
                bool found = false;

                for (int y = 1; y < map.Length - 1 && !found; y++)
                {
                    for (int x = 1; x < map[y].Length - 1; x++)
                    {
                        double spawnX = x + 0.5;
                        double spawnY = y + 0.5;

                        if (!IsValidSpawn(spawnX, spawnY))
                            continue;

                        // Genügend Abstand zum Spieler halten.
                        double dx = spawnX - px;
                        double dy = spawnY - py;

                        if (Math.Sqrt(dx * dx + dy * dy) < 2.0)
                            continue;

                        // Abstand zu anderen Gegnern halten.
                        bool occupied = enemies.Any(other =>
                            other != enemy &&
                            Math.Sqrt(
                                (other.X - spawnX) * (other.X - spawnX) +
                                (other.Y - spawnY) * (other.Y - spawnY)
                            ) < 0.8);

                        if (occupied)
                            continue;

                        enemy.X = spawnX;
                        enemy.Y = spawnY;
                        found = true;
                        break;
                    }
                }
            }
        }
    }
}