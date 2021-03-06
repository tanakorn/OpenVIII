﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS0649

namespace OpenVIII
{
    public enum MODULE : int
    {
        BATTLE = 3,
        BATTLE_SWIRL = 4,
        FIELD = 5,
        FIELD_DEBUG = -5,
        BATTLE_DEBUG = -3,
        MOVIETEST = -9,
        OVERTURE_DEBUG = -12,
        MAINMENU_DEBUG = -13,
        WORLD_DEBUG = -17,
        FACE_TEST = -20,
        ICON_TEST = -21,
        CARD_TEST = -22,
        FIELD_MODEL_TEST = -51,
    }

    /// <summary>
    /// Battle Speed Settings
    /// </summary>
    /// <see cref="https://gamefaqs.gamespot.com/ps/197343-final-fantasy-viii/faqs/58936"/>
    public enum BattleSpeed : byte
    {
        Fastest = 1,
        Fast,
        Normal,
        Slow,
        Slowest,
    }

    /// <summary>
    /// Speed Mod
    /// </summary>
    /// <see cref="https://gamefaqs.gamespot.com/ps/197343-final-fantasy-viii/faqs/58936"/>
    public enum SpeedMod : byte
    {
        Stop = 0,
        Slow = 1,
        Normal = 2,
        Haste = 3,
        AlwaysFull = 0xFF //not sure what i should set this too.
    }

    public static partial class Memory
    {
        public static float CameraScale { get; internal set; } = 100f;
        public static float BattleStageScale { get; internal set; } = 100f;
        public static float EnemyCoordinateScale { get; internal set; } = 100f;

        public static Log Log;
        public static bool EnableDumpingData = false;
        public static BattleSpeed CurrentBattleSpeed => Memory.State?.Configuration?.BattleSpeed ?? BattleSpeed.Normal;
        public static List<Task> FfccLeftOverTask = new List<Task>();

        public enum GraphicModes
        {
            OpenGL,
            DirectX
        };

        public static GraphicModes CurrentGraphicMode;

        //MonoGame
        public static GraphicsDeviceManager graphics;

        public static SpriteBatch spriteBatch;

        public static Core.ImGuiRenderer imgui;

        private static int mainThreadID;

        public static ContentManager content;

        public static bool IsActive = true;
        public static Font font;

        public static Task InitTask;
        //public static Texture2D[] iconsTex;

        public static Cards Cards;
        public static Card.Game Card_Game;
        public static Faces Faces;
        public static Icons Icons;
        public static Strings Strings;
        public static Kernel_bin Kernel_Bin;

        public static Texture2D shadowTexture;
        public static VertexPositionTexture[] shadowGeometry;

        public static Extended.Languages languages = Extended.Languages.en;

        public enum ScaleMode
        {
            /// <summary>
            /// scale object to have the same height as viewport
            /// </summary>
            FitVertical,

            /// <summary>
            /// scale object to have the same width as viewport
            /// </summary>
            FitHorizontal,

            /// <summary>
            /// Same as FitVertical unless width is too large, then it becomes FitHorizontal
            /// </summary>
            FitBoth,

            /// <summary>
            /// fill the entire viewport
            /// </summary>
            Stretch
        }

        public static bool ProcessActions(Action[] actions)
        {
            if (Threaded)
            {
                List<Task> tasks = new List<Task>(actions.Length);
                actions.ForEach(x => { if (!Token.IsCancellationRequested) tasks.Add(Task.Run(x, Token)); });
                //Some code that cannot be threaded on init.
                if (!Task.WaitAll(tasks.ToArray(), 10000))
                    throw new TimeoutException("Task took too long!");
            }
            else actions.ForEach(x => x.Invoke());
            return !Token.IsCancellationRequested;
        }

        public static IEnumerable<bool> ProcessFuncs(Func<bool>[] funcs)
        {
            if (Threaded)
            {
                List<Task<bool>> tasks = new List<Task<bool>>(funcs.Length);
                funcs.ForEach(x => { if (!Token.IsCancellationRequested) tasks.Add(Task.Run(x, Token)); });
                //Some code that cannot be threaded on init.
                if (!Task.WaitAll(tasks.ToArray(), 10000))
                    throw new TimeoutException("Task took too long!");
                return tasks.Select(x => x.Result);
            }
            else return funcs.Select(x => x.Invoke());
        }

        public static Point Center => new Point(graphics.GraphicsDevice.Viewport.Width / 2, graphics.GraphicsDevice.Viewport.Height / 2);
        public const ScaleMode _scaleMode = ScaleMode.Stretch;

        public static Vector2 Scale(float Width = PreferredViewportWidth, float Height = PreferredViewportHeight, ScaleMode scaleMode = _scaleMode, int targetX = 0, int targetY = 0)
        {
            if (targetX == 0)
                targetX = graphics.GraphicsDevice.Viewport.Width;
            if (targetY == 0)
                targetY = graphics.GraphicsDevice.Viewport.Height;
            float h = targetX / Width;
            float v = targetY / Height;
            switch (scaleMode)
            {
#pragma warning disable CS0162 // Unreachable code detected
                case ScaleMode.FitHorizontal:
                    return new Vector2(h, h);

                case ScaleMode.FitVertical:
                    return new Vector2(v, v);

                case ScaleMode.FitBoth:
                    return (v * Width > targetX) ? new Vector2(h, h) : new Vector2(v, v);

                case ScaleMode.Stretch:
                default:
                    return new Vector2(h, v);
#pragma warning restore CS0162 // Unreachable code detected
            }
        }

        //original resolution I am working on, therefore if user scales it we need to propertially scale everything
        public const int PreferredViewportWidth = 1280;

        public const int PreferredViewportHeight = 720;

        private static GameTime gameTime;

        public static GameTime GameTime
        {
            get => gameTime;
            set => gameTime = value;
        }

        public static TimeSpan TotalGameTime => gameTime?.TotalGameTime ?? TimeSpan.Zero;
        public static TimeSpan ElapsedGameTime => gameTime?.ElapsedGameTime ?? TimeSpan.Zero;
        public static TimeSpan DateTimeNow => TimeSpan.FromTicks(DateTime.Now.Ticks);

        private static ushort prevmusic = 0;
        private static ushort currmusic = 0;

        /// <summary>
        /// Stores current savestate. When you save this is wrote. When you load this is replaced.
        /// </summary>
        private static Saves.Data _state = new Saves.Data();

        public static ushort MusicIndex
        {
            get
            {
                if (dicMusic.Count > 0)
                {
                    ushort max = (ushort)dicMusic.Keys.Max();
                    ushort min = (ushort)dicMusic.Keys.Min();
                    while ((prevmusic > currmusic || prevmusic == ushort.MinValue && currmusic == ushort.MaxValue) &&
                        !dicMusic.ContainsKey((MusicId)currmusic))
                    {
                        if (max < currmusic)
                        {
                            currmusic = max;
                        }
                        else
                        {
                            currmusic--;
                        }
                    }
                    while (dicMusic.Count > 0 && prevmusic < currmusic && !dicMusic.ContainsKey((MusicId)currmusic))
                    {
                        if (max < currmusic)
                        {
                            currmusic = min;
                        }
                        else
                        {
                            currmusic++;
                        }
                    }
                    return currmusic;
                }
                else return 0;
            }
            set
            {
                prevmusic = currmusic;
                currmusic = value;
            }
        }

        public static readonly Dictionary<MusicId, List<string>> dicMusic = new Dictionary<MusicId, List<string>>(); //ogg and sgt files have same 3 digit prefix.

        public static void SpriteBatchStartStencil(SamplerState ss = null) =>

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.Opaque, ss ?? SamplerState.PointClamp, graphics.GraphicsDevice.DepthStencilState);

        public static void SpriteBatchStart(BlendState bs = null, SamplerState ss = null) =>

    spriteBatch.Begin(SpriteSortMode.Deferred, bs ?? BlendState.AlphaBlend, ss ?? SamplerState.PointClamp, graphics.GraphicsDevice.DepthStencilState);

        public static void SpriteBatchStartAlpha(SpriteSortMode sortMode = SpriteSortMode.Deferred, SamplerState ss = null, Matrix? tm = null) =>

            spriteBatch.Begin(sortMode: sortMode, blendState: BlendState.AlphaBlend, samplerState: ss ?? SamplerState.PointClamp, transformMatrix: tm);

        public static void SpriteBatchEnd() => spriteBatch.End();

        public static readonly BlendState blendState_BasicAdd = new BlendState()
        {
            ColorSourceBlend = Blend.SourceColor,
            ColorDestinationBlend = Blend.DestinationColor,
            ColorBlendFunction = BlendFunction.Add,

            AlphaSourceBlend = Blend.SourceAlpha,
            AlphaDestinationBlend = Blend.DestinationAlpha,
            AlphaBlendFunction = BlendFunction.Add
        };

        /// <summary>
        /// subtract. Used for windows/glass makes color darker of things behind this layer.
        /// </summary>
        /// <see cref="http://community.monogame.net/t/solved-custom-blendstate-advice/11006"/>
        public static readonly BlendState blendState_Subtract = new BlendState
        {
            ColorWriteChannels = ColorWriteChannels.Blue | ColorWriteChannels.Green | ColorWriteChannels.Red,
            ColorSourceBlend = Blend.One,
            //AlphaSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            //AlphaDestinationBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract
        };

        /// <summary>
        /// add. seems to work if colors are preblended like if they overlap the colors combined ahead of time. Used for light.
        /// </summary>
        /// <see cref="http://community.monogame.net/t/solved-custom-blendstate-advice/11006"/>
        public static readonly BlendState blendState_Add = new BlendState
        {
            ColorWriteChannels = ColorWriteChannels.Blue | ColorWriteChannels.Green | ColorWriteChannels.Red,
            ColorSourceBlend = Blend.One,
            //AlphaSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            //AlphaDestinationBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add
        };

        /// <summary>
        /// untested add with blendfactor. You set the GraphicsDevice.BlendFactor before drawing.
        /// </summary>
        public static readonly BlendState blendState_Add_BlendFactor = new BlendState
        {
            ColorWriteChannels = ColorWriteChannels.Blue | ColorWriteChannels.Green | ColorWriteChannels.Red,
            ColorSourceBlend = Blend.BlendFactor,
            //AlphaSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            //AlphaDestinationBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add
        };

        public static readonly BlendState blendState_forceDraw = new BlendState()
        {
            ColorSourceBlend = Blend.SourceColor,
            ColorDestinationBlend = Blend.SourceColor,
            ColorBlendFunction = BlendFunction.Add,

            AlphaSourceBlend = Blend.SourceAlpha,
            AlphaDestinationBlend = Blend.DestinationAlpha,
            AlphaBlendFunction = BlendFunction.Add,
        };

        private static MODULE module = MODULE.OVERTURE_DEBUG;

        public static string FF8DIR
        {
            get => _ff8Dir;
            private set => _ff8Dir = value;
        }

        public static string FF8DIRdata
        {
            get => _ff8DirData;
            private set => _ff8DirData = value;
        }

        public static string FF8DIRdata_lang { get; private set; }

        public static int InitTaskMethod(object obj)
        {
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(Memory)} :: {nameof(Init)}");
            CancellationToken token = (CancellationToken)obj;

            if (!token.IsCancellationRequested)
                Memory.Strings = new Strings();

            // requires strings because it uses an array generated in strings.
            // saves data will reference kernel_bin.
            if (!token.IsCancellationRequested)
                Kernel_Bin = new Kernel_bin();
            List<Action> actions = new List<Action>()
            {
                // this has a soft requirement on kernel_bin. It checks for null so should work without it.
                () => {MItems = Items_In_Menu.Read(); },
                Saves.Init,
                //loads all savegames from steam2013 or cd2000 or steam2019 directories. first come first serve.
                //TODO allow chosing of which save folder to use.
                InitStrings,
                //this initializes the field module, it's worth to have this at the beginning
                Fields.Initializer.Init,
                //this initializes the encounters
                Init_debugger_battle.Init,
            };

            if (graphics?.GraphicsDevice != null) // all below require graphics to work. to load textures graphics device needed.
            {
                actions.AddRange(new Action[]
                {
                    //this initializes the fonts and drawing system- holds fonts in-memory
                    () => { font = new Font(); },
                    // card images in menu.
                    () => { Cards = Cards.Load(); },

                    () => { Card_Game = new Card.Game(); },

                    () => { Faces = Faces.Load(); },

                    () => { Icons = Icons.Load(); },

                    () => { Magazines = Magazine.Load(); }
                });
            }
            actions.Add(() =>
            {
                InitState = Saves.Data.LoadInitOut();
                State = InitState?.Clone();
            });

            List<Task> tasks = new List<Task>();

            if (!token.IsCancellationRequested)
            {
                if (Threaded)
                {
                    foreach (Action a in actions)
                    {
                        if (!token.IsCancellationRequested)
                            tasks.Add(Task.Run(a, token));
                    }
                    Task.WaitAll(tasks.ToArray());
                }
                else
                    foreach (Action a in actions)
                    {
                        if (!token.IsCancellationRequested)
                            a.Invoke();
                    }
                if (graphics?.GraphicsDevice != null) // all below require graphics to work. to load textures graphics device needed.
                {
                    // requires font, faces, and icons. currently cards only used in debug menu. will
                    // have support for cards when added to menu.
                    if (!token.IsCancellationRequested)
                        Menu.InitStaticMembers();
                }
            }
            //EXE_Offsets test = new EXE_Offsets();
            Initiated = true;
            //ArchiveBase.PurgeCache();//remove files probably no longer needed.
            return 0;
        }

        public static bool Initiated { get; private set; } = false;
        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == mainThreadID;
        public static ConcurrentQueue<Action> MainThreadOnlyActions;

        public static void Init(GraphicsDeviceManager graphics, SpriteBatch spriteBatch, ContentManager content, string[] arguments)
        {
            if (Log == null) Log = new Log();
            Log.WriteLine($"{nameof(Memory)} :: {nameof(Init)}");
            Log.WriteLine($"{nameof(GraphicsDeviceManager)} :: {graphics}");
            Log.WriteLine($"{nameof(GraphicsDeviceManager)} :: {nameof(graphics.GraphicsDevice.Adapter.CurrentDisplayMode)} :: {graphics?.GraphicsDevice.Adapter.CurrentDisplayMode}");
            if (graphics != null)
                foreach (DisplayMode i in graphics.GraphicsDevice.Adapter.SupportedDisplayModes)
                    Log.WriteLine($"{nameof(GraphicsDeviceManager)} :: {nameof(graphics.GraphicsDevice.Adapter.SupportedDisplayModes)} :: {i}");
            //Log.WriteLine($"{nameof(GraphicsDeviceManager)} :: {graphics.GraphicsDevice.Adapter.DeviceName}");
            //Log.WriteLine($"{nameof(SpriteBatch)} :: {spriteBatch}");
            Log.WriteLine($"{nameof(ContentManager)} :: {content}");

            Memory.Log.WriteLine($"{nameof(Random)} :: new");
            Random = new Random((int)DateTime.Now.Ticks);
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(mainThreadID)} = {nameof(Thread)} :: {nameof(Thread.CurrentThread)} :: {nameof(Thread.ManagedThreadId)} = {Thread.CurrentThread.ManagedThreadId}");
            mainThreadID = Thread.CurrentThread.ManagedThreadId;
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(MainThreadOnlyActions)}");
            MainThreadOnlyActions = new ConcurrentQueue<Action>();

            FF8DIR = GameLocation.Current.DataPath;
            void SetData() =>
            FF8DIRdata = Extended.GetUnixFullPath(Path.Combine(FF8DIR, "Data"));

            SetData();
            bool languageSet = false;
            void setLang(string lang)
            {
                switch (lang.ToLower())
                {
                    case "2":
                    case "de":
                        languages = Extended.Languages.de;
                        break;

                    case "0":
                    case "en":
                        languages = Extended.Languages.en;
                        break;

                    case "3":
                    case "es":
                        languages = Extended.Languages.es;
                        break;

                    case "1":
                    case "fr":
                        languages = Extended.Languages.fr;
                        break;

                    case "4":
                    case "it":
                        languages = Extended.Languages.it;
                        break;

                    case "5":
                    case "jp":
                        languages = Extended.Languages.jp;
                        break;

                    default:
                        throw new InvalidEnumArgumentException($"{nameof(Memory)}::{nameof(Init)}::{nameof(lang)} ({lang}) is not a supported language code. (de,en,es,fr,it,jp)");
                }

                languageSet = true;
            }
            if (arguments != null && arguments.Length > 0)
            {
                IEnumerable<string[]> splitArguments = (from a in arguments
                                                        where a.Contains('=')
                                                        select a.Trim().Split(new[] { '=' }, 2)).OrderByDescending(x=>x[0],StringComparer.OrdinalIgnoreCase);
                foreach (string[] s in splitArguments)
                {
                    bool test(string @in, ref string @out)
                    {
                        if (!s[0].Equals(@in, StringComparison.OrdinalIgnoreCase)) return false;
                        @out = s[1].Trim(('"'));
                        return true;
                    }

                    string lang = "";
                    if (test("dir", ref _ff8Dir)) //override ff8 directory
                    {
                        if (!Directory.Exists(_ff8Dir))
                            throw new DirectoryNotFoundException(
                                $"{nameof(Memory)}::{nameof(Init)}::{nameof(arguments)}::{nameof(_ff8Dir)} ({s[0]}) Cannot find path: \"{_ff8Dir}\"");
                        SetData();
                    }
                    else if (test("data", ref _ff8DirData)) //override data folder location
                    {
                        if (!Directory.Exists(_ff8DirData))
                            throw new DirectoryNotFoundException(
                                $"{nameof(Memory)}::{nameof(Init)}::{nameof(arguments)}::{nameof(_ff8DirData)} ({s[0]}) Cannot find path: \"{_ff8DirData}\"");
                    }
                    else if (test("lang", ref lang)) //override language
                    {
                        setLang(lang);
                    }
                }
            }
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(FF8DIR)} = {FF8DIR}");
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(FF8DIRdata)} = {FF8DIRdata}");
            string langDatPath = Path.Combine(FF8DIR, "lang.dat");
            if (!languageSet && File.Exists(langDatPath))
                using (StreamReader streamReader = new StreamReader(
                    new FileStream(langDatPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), System.Text.Encoding.UTF8))
                {
                    string lang = streamReader.ReadLine()?.Trim();
                    setLang(lang);
                }

            Memory.Log.WriteLine($"{nameof(Extended)} :: {nameof(Extended.GetLanguageShort)} = {Extended.GetLanguageShort()}");
            string testDir = Extended.GetUnixFullPath(Path.Combine(FF8DIRdata, $"lang-{Extended.GetLanguageShort()}"));
            FF8DIRdata_lang = Directory.Exists(testDir) ? testDir : FF8DIRdata;
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(FF8DIRdata_lang)} = {FF8DIRdata_lang}");

            Archives.Init();

            Memory.graphics = graphics;
            Memory.spriteBatch = spriteBatch;
            Memory.content = content;
            Memory.Arguments = arguments;

            FF8StringReference.Init();
            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            Threaded = false;
            if (Threaded)
            {
                InitTask = new Task<int>(InitTaskMethod, Token);
                InitTask.Start();
            }
            else
                InitTaskMethod(Token);
        }

        public static string[] Arguments { get; set; }

        /// <summary>
        /// If true by the end of Update() will skip the next Draw()
        /// </summary>
        public static bool SuppressDraw { get; set; }

        public static bool IsMouseVisible { get; set; } = false;

        public static Saves.Data PrevState { get; set; }

        public static Saves.Data State
        {
            get => _state; set
            {
                _state = value;
                if (_state != null)
                    _state.LoadTime = Memory.gameTime?.TotalGameTime ?? new TimeSpan();
            }
        }

        public static CancellationTokenSource TokenSource { get; private set; }
        public static CancellationToken Token { get; private set; }
        public static Items_In_Menu MItems { get; private set; }
        public static Magazine Magazines { get; private set; }
        public static Saves.Data InitState { get; private set; }

        public static event EventHandler<MODULE> ModuleChangeEvent;

        public static MODULE Module
        {
            get => module; set
            {
                if (module != value)
                {
                    module = value;
                    ModuleChangeEvent?.Invoke(null, value);
                }
            }
        }

        #region battleProvider

        /// <summary>
        /// Active battle encounter. Set by field or battle module. You shouldn't change it in-battle.
        /// </summary>
        //public static int battle_encounter = 0;

        /// <summary>
        /// Battle music pointer. Set by SETBATTLEMUSIC in field module or by world module. Default=6
        /// </summary>
        public static int SetBattleMusic = 6;

        public static Battle.Encounters Encounters { get; set; }
        public static bool Threaded { get; private set; } = true;

        #endregion battleProvider

        #region MusicDataMidi

        public static Dictionary<ushort, string> Songssgt = new Dictionary<ushort, string>()
        {
            {0, "Lose" },
            {1, "Win" },
            {2, "Open" },
            {3, "Combat" },
            {4, "Run" },
            {5, "Battle" },
            {6, "Funsui" },
            {7, "End" },
            {8, "Antenna" },
            {9, "Waiting" },
            {10, "Ante " },
            {11, "Wind" },
            {12, "Crab" },
            {13, "Battle2" },
            {14, "Friend2" },
            {15, "Fuan2" },
            {16, "March2" },
            {17, "Land" },
            {18, "Julia" },
            {19, "Waltz" },
            {20, "Friend " },
            {21, "Dungeon" },
            {22, "Pianosolo" },
            {23, "Parade" },
            {24, "March1" },
            {25, "Secret" },
            {26, "Garden" },
            {27, "Fuan " },
            {28, "Polka2" },
            {29, "Anthem" },
            {30, "FlangChorus" },
            {31, "DubChorus" },
            {32, "SoloChorus" },
            {33, "FemaleChorus" },
            {34, "Chorus" },
            {35, "M7F5" },
            {36, "Sorceress" },
            {37, "Reet" },
            {38, "Soyo" },
            {39, "Rouka" },
            {40, "Night" },
            {41, "Field" },
            {42, "Guitar" },
            {43, "Concert" },
            {44, "Sea" },
            {45, "Silent" },
            {46, "Resistance" },
            {47, "Kaiso" },
            {48, "Horizon" },
            {49, "Master" },
            {50, "Battle2" },
            {51, "Rinoa" },
            {52, "Trabia" },
            {53, "Horizon2" },
            {54, "Truth" },
            {55, "Prison" },
            {56, "GalbadiaGarden" },
            {57, "Timber" },
            {58, "Galbadia " },
            {59, "Pinchi" },
            {60, "Scene1" },
            {61, "Pub" },
            {62, "Bat3" },
            {63, "Stage" },
            {64, "Choco" },
            {65, "White" },
            {66, "Majomv" },
            {67, "Musho" },
            {68, "Missile" },
            {69, "Speech" },
            {70, "Card" },
            {71, "Gomon" },
            {72, "Soto" },
            {73, "Majobat" },
            {74, "Train" },
            {75, "Garden2" },
            {76, "Bossbat2" },
            {77, "LastDungeon" },
            {78, "Gafly" },
            {79, "Demo" },
            {80, "Spy" },
            {81, "VoiceDeChocobo" },
            {82, "Salt" },
            {83, "Alien" },
            {84, "Sekichu" },
            {85, "Esta" },
            {86, "Moonmv" },
            {87, "Mdmotor" },
            {88, "Moonmv2" },
            {89, "Fly" },
            {90, "BossBat1" },
            {91, "Rag1" },
            {92, "Rag2" },
            {93, "LastBoss" },
            {94, "Lastwhite" },
            {95, "Lasbl" },
            {96, "Keisho" },
            {97, "Compression" },
        };

        #endregion MusicDataMidi

        #region MusicDataOGG

        public static Dictionary<ushort, FF8String> Songsogg;

        private static void InitStrings()
        {//is there Songogg being used for anything?
            Memory.Log.WriteLine($"{nameof(Memory)} :: {nameof(InitStrings)} :: {nameof(Songsogg)} ");
            Songsogg = new Dictionary<ushort, FF8String>()
            {
                {0,"Lose" },
                {1,"The Winner" },
                {4,"Never Look Back" },
                {5,"Don't Be Afraid" },
                {7,"Dead End" },
                {8,"Starting Up" },
                {9,"Intruders" },
                {12,"Don't Be Afraid (X-ATM092)" },
                {13,"Force Your Way" },
                {14,"FITHOS LUSEC WECOS VINOSEC (No Intro)" },
                {15,"Unrest" },
                {16,"The Stage is Set" },
                {17,"The Landing" },
                {18,"Love Grows" },
                {19,"Waltz for the Moon" },
                {20,"Ami" },
                {21,"Find Your Way" },
                {22,"Julia" },
                {23,"FITHOS LUSEC WECOS VINOSEC" },
                {24,"SeeD" },
                {25,"Tell Me" },
                {26,"Balamb GARDEN" },
                {27,"Fear" },
                {28,"Dance with the Balamb-Fish" },
                {29,"Cactus Jack" },
                {35,"The Mission" },
                {36,"SUCCESSION OF WITCHES" },
                {41,"Blue Fields" },
                {42,"Breezy" },
                {43,"Concert" },
                {46,"Timber Owls" },
                {47,"Fragments of Memories" },
                {48,"Fisherman's Horizon" },
                {49,"Heresy" },
                {51,"My Mind" },
                {52,"Where I Belong" },
                {53,"Starting Up (Looped)" },
                {54,"Truth" },
                {55,"Trust Me" },
                {56,"Galbadia GARDEN" },
                {57,"Martial Law" },
                {58,"Under Her Control" },
                {59,"Only a Plank Between One and Perdition" },
                {60,"Junction" },
                {61,"Roses and Wine" },
                {62,"The Man with the Machine Gun" },
                {63,"A Sacrifice" },
                {64,"ODEKA ke Chocobo" },
                {65,"Drifting" },
                {66,"Wounded" },
                {67,"Jailed" },
                {68,"Retaliation" },
                {69,"The Oath" },
                {70,"Shuffle or Boogie" },
                {71,"Rivals" },
                {72,"Blue Sky" },
                {73,"Premonition" },
                {75,"Galbadia GARDEN (No Intro)" },
                {76,"Maybe I'm a Lion" },
                {77,"The Castle" },
                {78,"Movin'" },
                {79,"Overture" },
                {80,"The Spy" },
                {81,"Mods de Chocobo" },
                {82,"The Salt Flats" },
                {83,"The Residents" },
                {84,"Lunatic Pandora" },
                {85,"Silence and Motion" },
                {86,"Tears of the Moon" },
                {88,"Tears of the Moon (Alternate)" },
                {89,"Ride On" },
                {90,"The Legendary Beast" },
                {91,"Slide Show Part 1" },
                {92,"Slide Show Part 2" },
                {93,"The Extreme" },
                {96,"The Successor" },
                {97,"Compression of Time" },
                {99,"The Landing (No Intro)" },
                {512,"The Loser" },
                {513,"Eyes on Me" },
                {514,"Irish Jig (Concert)" },
                {515,"Eyes on Me (Concert)" },
                {516,"Movin' (No Intro)" },
                {517,"The Landing (Alternate)" },
                {518,"The Landing (Alternate - No Intro)" },
                {519,"Galbadia GARDEN (Alternate)" },
            };
            DrawPointMagic = new Dictionary<byte, FF8String>()
        {
            {0, "Cure - Balamb Garden courtyard"},
            {1, "Blizzard - Balamb Garden training center"},
            {2, "Full-Life - Balamb Garden MD level"},
            {3, "Esuna - Balamb Garden library next to the book shelf"},
            {4, "Demi - Balamb Garden cafeteria (only during Garden Riot)"},
            {5, "Bio - Balamb Garden B2 floor"},
            {6, "Thunder - Balamb outside junk shop"},
            {7, "Cure - Balamb harbor"},
            {8, "Fire - Fire Cavern"},
            {9, "Silence - Dollet town square"},
            {10, "Blind - Dollet Communications Tower"},
            {11, "Scan - Timber Pub Aurora back alley"},
            {12, "Cure - Timber outside the pub"},
            {13, "Blizzaga - Timber Maniacs Building left room"},
            {14, "Haste - Galbadia Garden lobby"},
            {15, "Life - Galbadia Garden changing rooms"},
            {16, "Shell - Galbadia Garden courtyard"},
            {17, "Protect - Galbadia Garden ice rink"},
            {18, "Double - Galbadia Garden auditorium"},
            {19, "Aura - Outside Galbadia Garden during Garden war"},
            {20, "Cure - Timber forests in a Laguna dream"},
            {21, "Water - Timber forests in a Laguna dream"},
            {22, "Thundara - Deling City park"},
            {23, "Zombie - Deling City Sewers"},
            {24, "Esuna - Deling City Sewers"},
            {25, "Bio - Deling City Sewers"},
            {26, "Fira"},
            {27, "Berserk - D-District Prison Floor 9 - right cell"},
            {28, "Thundaga - D-District Prison Floor 11 - right cell"},
            {29, "Aero - Outside D-District Prison"},
            {30, "Blizzara - Missile Base - control room"},
            {31, "Blind - Missile Base room with G-Soldiers who ask to deliver a message"},
            {32, "Full-Life - Missile Base - silo room"},
            {33, "Drain - Winhill road south from town square"},
            {34, "Dispel - Winhill town square"},
            {35, "Curaga - Winhill Laguna's room in the dream"},
            {36, "Reflect - Winhill east road"},
            {37, "Protect - Tomb of the Unknown King - outside"},
            {38, "Float - Tomb of the Unknown King - north room"},
            {39, "Cura - Tomb of the Unknown King - east room"},
            {40, "Haste - Fishermans Horizon abandoned train station"},
            {41, "Shell - Fishermans Horizon junk shop"},
            {42, "Regen - Fishermans Horizon overlooking the sun panel"},
            {43, "Full-Life - Fishermans Horizon Master Fisherman's fishing spot"},
            {44, "Ultima - Fishermans Horizon mayor's house"},
            {45, "Thundaga - Great Salt Lake past the dinosaur skeleton"},
            {46, "Meteor - Great Salt Lake dinosaur skeleton"},
            {47, "Curaga - Esthar city streets near city entrance"},
            {48, "Blizzard - Esthar outside palace"},
            {49, "Quake - Esthar outside Odine's Lab"},
            {50, "Tornado - Esthar shopping mall"},
            {51, "Double - Esthar Odine's Lab in a Laguna dream"},
            {52, "Pain"},
            {53, "Flare - Esthar Odine's Lab in a Laguna dream"},
            {54, "Stop - Sorceress Memorial"},
            {55, "Stop"},
            {56, "Life - Tears' Point entrance"},
            {57, "Reflect - Tears' Point middle"},
            {58, "Death - Lunatic Pandora Laboratory in a Laguna dream"},
            {59, "Holy - Lunatic Pandora near Elevator #1"},
            {60, "Silence - Lunatic Pandora"},
            {61, "Ultima - Lunatic Pandora"},
            {62, "Confuse"},
            {63, "Break - Lunatic Pandora on the way to fight Adel"},
            {64, "Meteor - Lunatic Pandora entrance"},
            {65, "Curaga - Lunatic Pandora elevator room"},
            {66, "Slow"},
            {67, "Curaga - Edea's Orphanage"},
            {68, "Flare"},
            {69, "Holy"},
            {70, "Sleep - Centra Excavation Site"},
            {71, "Confuse - Centra Excavation Site"},
            {72, "Aero - Centra Ruins right ladder after the lift"},
            {73, "Drain - Centra Ruins platform after the first staircase"},
            {74, "Pain - Centra Ruins next to the dome"},
            {75, "Thundaga - Trabia Garden in front of the statue"},
            {76, "Zombie - Trabia Garden cemetery"},
            {77, "Aura - Trabia Garden stage"},
            {78, "Ultima - Shumi Village - above ground"},
            {79, "Blizzaga - Shumi Village - outside elder's house"},
            {80, "Firaga - Shumi Village workshop"},
            {81, "Tornado"},
            {82, "Holy - White SeeD Ship"},
            {83, "Cura - Ragnarok room with a red Propagator"},
            {84, "Life - Ragnarok hangar upstairs"},
            {85, "Full-Life - Ragnarok room with save point"},
            {86, "Dispel - Deep Sea Research Center second level"},
            {87, "Esuna - Deep Sea Research Center secret room"},
            {88, "Triple - Deep Sea Research Center third screen on the way to Ultima Weapon's lair"},
            {89, "Ultima - Deep Sea Research Center fifth screen on the way to Ultima Weapon's lair"},
            {90, "Meltdown - Lunar Base room before the escape pods"},
            {91, "Meteor - Lunar Base Ellone's room"},
            {92, "Haste"},
            {93, "Slow"},
            {94, "Curaga"},
            {95, "Life"},
            {96, "Stop"},
            {97, "Regen"},
            {98, "Double"},
            {99, "Triple"},
            {100, "Flare - Ultimecia Castle outside"},
            {101, "Curaga - Ultimecia Castle storage room"},
            {102, "Cura - Ultimecia Castle passageway"},
            {103, "Scan"},
            {104, "Esuna"},
            {105, "Slow - Ultimecia Castle courtyard"},
            {106, "Dispel - Ultimecia Castle chapel"},
            {107, "Stop - Ultimecia Castle clock tower"},
            {108, "Life"},
            {109, "Flare"},
            {110, "Aura - Ultimecia Castle wine cellar"},
            {111, "Holy - Ultimecia Castle treasure room"},
            {112, "Meteor"},
            {113, "Meltdown - Ultimecia Castle art gallery"},
            {114, "Ultima - Ultimecia Castle armory"},
            {115, "Full-Life - Ultimecia Castle prison"},
            {116, "Triple"},
            {117, "Fire"},
            {118, "Fire"},
            {119, "Fire"},
            {120, "Fire"},
            {121, "Fire"},
            {122, "Fire"},
            {123, "Fire"},
            {124, "Fire"},
            {125, "Fire"},
            {126, "Fire"},
            {127, "Fire"},
            {128, "Cure"},
            {129, "Esuna"},
            {130, "Thunder"},
            {131, "Fira"},
            {132, "Thundara"},
            {133, "Blizzara"},
            {134, "Blizzard"},
            {135, "Fire"},
            {136, "Cure"},
            {137, "Water"},
            {138, "Cura"},
            {139, "Esuna"},
            {140, "Scan"},
            {141, "Shell"},
            {142, "Haste"},
            {143, "Aero"},
            {144, "Bio"},
            {145, "Life"},
            {146, "Demi"},
            {147, "Protect"},
            {148, "Holy"},
            {149, "Thundaga"},
            {150, "Stop"},
            {151, "Firaga"},
            {152, "Regen"},
            {153, "Blizzaga"},
            {154, "Confuse"},
            {155, "Flare"},
            {156, "Dispel"},
            {157, "Slow"},
            {158, "Quake"},
            {159, "Curaga"},
            {160, "Tornado"},
            {161, "Full-Life"},
            {162, "Reflect"},
            {163, "Aura"},
            {164, "Quake"},
            {165, "Double"},
            {166, "Break"},
            {167, "Meteor"},
            {168, "Ultima"},
            {169, "Triple"},
            {170, "Confuse"},
            {171, "Blind"},
            {172, "Quake"},
            {173, "Sleep"},
            {174, "Silence"},
            {175, "Flare"},
            {176, "Death"},
            {177, "Drain"},
            {178, "Pain"},
            {179, "Berserk"},
            {180, "Float"},
            {181, "Zombie"},
            {182, "Meltdown"},
            {183, "Ultima"},
            {184, "Tornado"},
            {185, "Quake"},
            {186, "Meteor"},
            {187, "Holy"},
            {188, "Flare"},
            {189, "Aura"},
            {190, "Ultima"},
            {191, "Triple"},
            {192, "Full-Life"},
            {193, "Tornado"},
            {194, "Quake"},
            {195, "Meteor"},
            {196, "Holy"},
            {197, "Flare"},
            {198, "Aura"},
            {199, "Ultima"},
            {200, "Triple"},
            {201, "Full-Life"},
            {202, "Tornado"},
            {203, "Quake"},
            {204, "Meteor"},
            {205, "Holy"},
            {206, "Flare"},
            {207, "Aura"},
            {208, "Ultima"},
            {209, "Triple"},
            {210, "Full-Life"},
            {211, "Ultima"},
            {212, "Meteor"},
            {213, "Holy"},
            {214, "Flare"},
            {215, "Aura"},
            {216, "Ultima"},
            {217, "Triple"},
            {218, "Full-Life"},
            {219, "Meteor"},
            {220, "Holy"},
            {221, "Triple"},
            {222, "Aura"},
            {223, "Ultima"},
            {224, "Triple"},
            {225, "Full-Life"},
            {226, "Meteor"},
            {227, "Holy"},
            {228, "Flare"},
            {229, "Aura"},
            {230, "Ultima"},
            {231, "Triple"},
            {232, "Full-Life"},
            {233, "Meteor"},
            {234, "Triple"},
            {235, "Flare"},
            {236, "Aura"},
            {237, "Ultima"},
            {238, "Triple"},
            {239, "Full-Life"},
            {240, "Meteor"},
            {241, "Holy"},
            {242, "Flare"},
            {243, "Aura"},
            {244, "Ultima"},
            {245, "Blizzard"},
            {246, "Cure"},
            {247, "Dispel"},
            {248, "Confuse"},
            {249, "Meteor"},
            {250, "Double"},
            {251, "Aura"},
            {252, "Holy"},
            {253, "Flare"},
            {254, "Ultima"},
            {255, "Scan"}
        };
        }

        #endregion MusicDataOGG

        #region DrawPointMagic

        public static Dictionary<byte, FF8String> DrawPointMagic;
        public static Input2 Input2;

        /// <summary>
        /// Random number generator seeded with time.
        /// </summary>
        /// <remarks>creates global random class for all sort of things</remarks>
        public static Random Random = null;

        public static int Year = 2013; // need to dynamicly detect if 2000/2013/2019, maybe need 2000 1.2 as well.
        private static string _ff8Dir;
        private static string _ff8DirData;

        #endregion DrawPointMagic

        public static void Update()
        {
            Action a = null;
            while (IsMainThread && (MainThreadOnlyActions?.TryDequeue(out a) ?? false))
            { a.Invoke(); }
            for (int i = 0; IsMainThread && i < FfccLeftOverTask.Count; i++)
            {
                if (FfccLeftOverTask[i].IsCompleted)
                {
                    FfccLeftOverTask[i].Dispose();
                    FfccLeftOverTask.RemoveAt(i--);
                }
            }
        }

        public static class FieldHolder
        {
            //public static string[] MapList;
            public static ushort FieldID = 756;

            public static string[] fields;
            //public static int[] FieldMemory;

            public static string GetString(ushort? inputFieldID = null) => fields?.ElementAtOrDefault(inputFieldID ?? FieldID);
        }
    }
}