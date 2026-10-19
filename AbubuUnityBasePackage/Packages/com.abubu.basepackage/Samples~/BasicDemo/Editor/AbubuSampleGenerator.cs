using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Abubu.Audio;
using Abubu.Effects;
using Abubu.Samples;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zenject;

namespace Abubu.Editor
{
    /// <summary>
    /// Tools > Abubu > Create Sample Scenes
    /// 動作確認用のサンプル一式 (シーン 2 つ・効果音 WAV・エフェクト / キューブのプレハブ・サンプル専用 SoundLibrary) を生成する。
    /// 何度実行しても上書き生成されるだけで安全。
    /// </summary>
    /// <remarks>
    /// 既存プロジェクトの設定をなるべく汚さないための方針:
    /// <list type="bullet">
    /// <item>生成物はすべてサンプルのフォルダ (Import 先の Basic Demo/Generated) に置く。</item>
    /// <item>サンプルのサウンドはサンプル専用の SoundLibrary に登録し、プロジェクトの SoundLibrary には includes で 1 件参照を足すだけ
    /// (プロジェクトのキーは書き換えない。Remove Sample Registration で外せる)。</item>
    /// <item>エフェクトも同様に、サンプル専用の EffectLibrary に登録し、プロジェクトの EffectLibrary には includes で参照を 1 件足すだけ (同じキーはプロジェクト側が優先)。</item>
    /// <item>Build Profiles (Build Settings) へのシーン追加は確認ダイアログで選んだときだけ。
    /// 追加しなくても、エディタ上ではシーンのパス指定で遷移できる。</item>
    /// <item>変更内容は実行前にダイアログで一覧表示する。</item>
    /// </list>
    /// </remarks>
    public static class AbubuSampleGenerator
    {
        /// <summary>サンプルの場所が特定できなかったときの生成先</summary>
        private const string FallbackFolder = "Assets/Abubu/Samples";
        private const int SampleRate = 22050;
        private const string EffectKey = "burst";

        /// <summary>生成先フォルダ (Generate の最初に決まる)</summary>
        private static string Folder = FallbackFolder;

        private static string SampleSoundLibraryPath => $"{Folder}/SampleSoundLibrary.asset";
        private static string SampleEffectLibraryPath => $"{Folder}/SampleEffectLibrary.asset";

        [MenuItem("Tools/Abubu/Create Sample Scenes", priority = 20)]
        public static void Generate()
        {
            Folder = ResolveFolder();
            var settings = FindSettings();
            var soundLibrary = settings != null ? settings.Sound.Library : null;
            var effectLibrary = settings != null ? settings.Effects.Library : null;

            // 何を作り、何を変更するかを先に見せる
            var plan = new List<string>
            {
                $"・生成: {Folder}/ (シーン 2 つ・WAV 7 つ・プレハブ 2 つ・マテリアル 2 つ・SampleSoundLibrary)",
            };
            if (settings == null)
            {
                plan.Add("・AbubuSettings が見つからないため Tools/Abubu/Setup Project を実行 " +
                         "(Assets/Abubu に設定アセット、Resources/ProjectContext.prefab に AbubuInstaller を作成・追加)");
            }
            plan.Add(soundLibrary != null
                ? $"・{AssetDatabase.GetAssetPath(soundLibrary)} の includes に SampleSoundLibrary を追加 (既存のキーは変更しません)"
                : "・AbubuSettings の Sound Library が空なら SoundLibrary を割り当て、その includes に SampleSoundLibrary を追加");
            plan.Add(effectLibrary != null
                ? $"・{AssetDatabase.GetAssetPath(effectLibrary)} の includes に SampleEffectLibrary を追加 (既存のキーは変更しません)"
                : "・AbubuSettings の Effect Library が空なら SampleEffectLibrary を割り当て");

            if (!EditorUtility.DisplayDialog("Abubu Sample",
                    "動作確認用のサンプルを生成します。\n\n" + string.Join("\n", plan) +
                    "\n\n不要になったら Tools/Abubu/Remove Sample Registration でライブラリへの登録を外し、フォルダを削除してください。",
                    "生成する", "キャンセル"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (settings == null)
            {
                AbubuProjectSetup.Setup();
                settings = FindSettings();
                if (settings == null)
                {
                    Debug.LogError("[Abubu] AbubuSettings が見つからないため、サンプルを生成できませんでした。");
                    return;
                }
            }
            EnsureLibraries(settings);

            EnsureFolder(Folder + "/Audio");

            try
            {
                AssetDatabase.StartAssetEditing();
                WriteAudio();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            RegisterSounds(settings);
            var burst = CreateBurstPrefab();
            RegisterEffect(settings, burst);
            var cube = CreateCubePrefab();

            var titlePath = $"{Folder}/{SampleTitleController.SceneName}.unity";
            var gamePath = $"{Folder}/{SampleGameController.SceneName}.unity";
            CreateTitleScene(titlePath, gamePath);
            CreateGameScene(gamePath, titlePath, cube);

            AssetDatabase.SaveAssets();

            // Build Profiles への追加は任意 (追加しなくてもエディタではパス指定で遷移できる)
            if (EditorUtility.DisplayDialog("Abubu Sample",
                    "サンプルの 2 シーンを Build Profiles (Build Settings) のシーン一覧に追加しますか?\n\n" +
                    "エディタ上で試すだけなら追加は不要です。\n" +
                    "追加するとビルドに含まれるため、リリース前に一覧から外してください。",
                    "追加する", "追加しない"))
            {
                AddToBuildSettings(titlePath, gamePath);
            }

            EditorSceneManager.OpenScene(titlePath);
            Debug.Log($"[Abubu] サンプルを生成しました: {titlePath} を開いて再生してください。");
        }

        /// <summary>
        /// Create Sample Scenes でプロジェクトのライブラリに加えた登録 (SampleSoundLibrary / SampleEffectLibrary の include) を外す。
        /// 生成したフォルダは消さない (必要なら手動で削除する)。
        /// </summary>
        [MenuItem("Tools/Abubu/Remove Sample Registration", priority = 21)]
        public static void RemoveRegistration()
        {
            Folder = ResolveFolder();
            var settings = FindSettings();
            if (settings == null)
            {
                Debug.Log("[Abubu] AbubuSettings が見つかりません。外す登録はありません。");
                return;
            }

            var removed = new List<string>();
            var sampleLibrary = AssetDatabase.LoadAssetAtPath<SoundLibrary>(SampleSoundLibraryPath);
            if (settings.Sound.Library != null && sampleLibrary != null && settings.Sound.Library != sampleLibrary)
            {
                var so = new SerializedObject(settings.Sound.Library);
                var includes = so.FindProperty("includes");
                for (var i = includes.arraySize - 1; i >= 0; i--)
                {
                    if (includes.GetArrayElementAtIndex(i).objectReferenceValue != sampleLibrary) continue;
                    includes.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    includes.DeleteArrayElementAtIndex(i);
                    removed.Add($"{AssetDatabase.GetAssetPath(settings.Sound.Library)} の includes から SampleSoundLibrary");
                }
                so.ApplyModifiedProperties();
            }

            var sampleEffectLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(SampleEffectLibraryPath);
            if (settings.Effects.Library != null && sampleEffectLibrary != null && settings.Effects.Library != sampleEffectLibrary)
            {
                var so = new SerializedObject(settings.Effects.Library);
                var includes = so.FindProperty("includes");
                for (var i = includes.arraySize - 1; i >= 0; i--)
                {
                    if (includes.GetArrayElementAtIndex(i).objectReferenceValue != sampleEffectLibrary) continue;
                    includes.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    includes.DeleteArrayElementAtIndex(i);
                    removed.Add($"{AssetDatabase.GetAssetPath(settings.Effects.Library)} の includes から SampleEffectLibrary");
                }
                so.ApplyModifiedProperties();
            }

            AssetDatabase.SaveAssets();
            Debug.Log(removed.Count > 0
                ? "[Abubu] サンプルの登録を外しました:\n" + string.Join("\n", removed) + $"\n生成物は {Folder} に残っています (不要なら削除してください)。"
                : "[Abubu] 外すサンプルの登録はありませんでした。");
        }

        // ---------------------------------------------------------------- Audio

        private static void WriteAudio()
        {
            WriteWav("jump", 0.25f, t => Sine(Lerp(300f, 900f, t / 0.25f), t) * Decay(t, 0.25f) * 0.6f);
            WriteWav("coin", 0.3f, t => Square(t < 0.08f ? 988f : 1319f, t) * Decay(t, 0.3f) * 0.3f);
            var random = new System.Random(1);
            float lowpassed = 0f;
            WriteWav("explosion", 0.9f, t =>
            {
                lowpassed = Mathf.Lerp(lowpassed, (float)(random.NextDouble() * 2 - 1), 0.15f);
                return lowpassed * Decay(t, 0.9f, 4f) * 1.6f;
            });

            // BGM: 4 小節ループのアルペジオ
            WriteWav("bgm_a", 8f, t => Arpeggio(t, 120f, new[] { 261.6f, 329.6f, 392f, 523.3f, 349.2f, 440f, 523.3f, 392f }) * 0.25f);
            WriteWav("bgm_b", 8f, t => Arpeggio(t, 150f, new[] { 220f, 261.6f, 329.6f, 440f, 196f, 246.9f, 293.7f, 392f }) * 0.25f);

            // 環境音: ループの継ぎ目で途切れないよう、変調周期をクリップ長の約数にしている
            float wind = 0f;
            WriteWav("wind", 6f, t =>
            {
                wind = Mathf.Lerp(wind, (float)(random.NextDouble() * 2 - 1), 0.02f);
                return wind * (0.6f + 0.4f * Mathf.Sin(t * Mathf.PI * 2f / 3f)) * 3f;
            });
            WriteWav("rain", 6f, t => (float)(random.NextDouble() * 2 - 1) * (random.NextDouble() < 0.02 ? 0.5f : 0.08f));
        }

        private static float Arpeggio(float t, float bpm, float[] notes)
        {
            var step = 60f / bpm / 2f; // 8 分音符
            var index = (int)(t / step);
            var local = t - index * step;
            var freq = notes[index % notes.Length];
            var bass = notes[(index / 8 % 2) * 4] / 2f;
            return Triangle(freq, t) * Decay(local, step, 3f) + Triangle(bass, t) * 0.4f;
        }

        private static float Sine(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);
        private static float Square(float f, float t) => Mathf.Sign(Sine(f, t));
        private static float Triangle(float f, float t) => 2f * Mathf.Abs(2f * (t * f - Mathf.Floor(t * f + 0.5f))) - 1f;
        private static float Decay(float t, float length, float power = 1.5f) => Mathf.Pow(Mathf.Clamp01(1f - t / length), power);
        private static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);

        private static void WriteWav(string name, float seconds, Func<float, float> generator)
        {
            var count = (int)(seconds * SampleRate);
            using var stream = new FileStream($"{Folder}/Audio/{name}.wav", FileMode.Create);
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + count * 2);
            writer.Write("WAVEfmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);          // PCM
            writer.Write((short)1);          // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(count * 2);
            for (var i = 0; i < count; i++)
            {
                var v = Mathf.Clamp(generator(i / (float)SampleRate), -1f, 1f);
                writer.Write((short)(v * short.MaxValue));
            }
        }

        /// <summary>
        /// サンプルのサウンドをサンプル専用の SoundLibrary に登録し、プロジェクトの SoundLibrary から includes で参照させる。
        /// プロジェクトのライブラリのエントリ自体は変更しない (同じキーがあればプロジェクト側が優先される)。
        /// </summary>
        private static void RegisterSounds(AbubuSettings settings)
        {
            var sampleLibrary = LoadOrCreate<SoundLibrary>(SampleSoundLibraryPath);
            var so = new SerializedObject(sampleLibrary);
            AddSound(so, "bgm", "bgm_a", 0.8f);
            AddSound(so, "bgm", "bgm_b", 0.8f);
            AddSound(so, "se", "jump", 1f, pitch: new Vector2(0.9f, 1.1f));
            AddSound(so, "se", "coin", 0.8f, cooldown: 0.05f);
            AddSound(so, "se", "explosion", 1f);
            AddSound(so, "ambient", "wind", 0.6f);
            AddSound(so, "ambient", "rain", 0.5f);
            so.ApplyModifiedPropertiesWithoutUndo();

            var projectLibrary = settings.Sound.Library;
            if (projectLibrary == sampleLibrary) return;

            var projectSo = new SerializedObject(projectLibrary);
            var includes = projectSo.FindProperty("includes");
            for (var i = 0; i < includes.arraySize; i++)
            {
                if (includes.GetArrayElementAtIndex(i).objectReferenceValue == sampleLibrary) return;
            }
            includes.arraySize++;
            includes.GetArrayElementAtIndex(includes.arraySize - 1).objectReferenceValue = sampleLibrary;
            projectSo.ApplyModifiedProperties();
        }

        private static void AddSound(SerializedObject library, string list, string key, float volume, Vector2? pitch = null, float cooldown = 0f)
        {
            var array = library.FindProperty(list);
            SerializedProperty element = null;
            for (var i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).FindPropertyRelative("Key").stringValue == key) element = array.GetArrayElementAtIndex(i);
            }
            if (element == null)
            {
                array.arraySize++;
                element = array.GetArrayElementAtIndex(array.arraySize - 1);
            }

            element.FindPropertyRelative("Key").stringValue = key;
            var clips = element.FindPropertyRelative("Clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Folder}/Audio/{key}.wav");
            element.FindPropertyRelative("Volume").floatValue = volume;
            element.FindPropertyRelative("PitchRange").vector2Value = pitch ?? Vector2.one;
            element.FindPropertyRelative("Cooldown").floatValue = cooldown;
            element.FindPropertyRelative("SpatialBlend").floatValue = 0f;
        }

        // ---------------------------------------------------------------- Prefabs

        private static GameObject CreateBurstPrefab()
        {
            var material = SaveMaterial("SampleParticle", "Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit");
            var go = new GameObject("SampleBurst");
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.duration = 0.3f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.3f), new Color(1f, 0.35f, 0.1f));
                main.gravityModifier = 0.5f;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.2f;

                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
                return PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/SampleBurst.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static GameObject CreateCubePrefab()
        {
            var material = SaveMaterial("SampleCube", "Universal Render Pipeline/Lit", "Standard");
            material.color = new Color(0.3f, 0.7f, 1f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                go.name = "SampleCube";
                go.transform.localScale = Vector3.one * 0.6f;
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.AddComponent<Rigidbody>();
                return PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/SampleCube.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Material SaveMaterial(string name, params string[] shaderNames)
        {
            var path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var shader = shaderNames.Select(Shader.Find).FirstOrDefault(s => s != null);
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// エフェクト "burst" をサンプル専用の EffectLibrary に登録し、プロジェクトの EffectLibrary から includes で参照させる
        /// (サウンドと同じ方式。プロジェクトのライブラリのエントリには触らない。同じキーがあればプロジェクト側が優先される)。
        /// </summary>
        private static void RegisterEffect(AbubuSettings settings, GameObject prefab)
        {
            var sampleLibrary = LoadOrCreate<EffectLibrary>(SampleEffectLibraryPath);
            var so = new SerializedObject(sampleLibrary);
            var array = so.FindProperty("effects");
            SerializedProperty element = null;
            for (var i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).FindPropertyRelative("Key").stringValue == EffectKey) element = array.GetArrayElementAtIndex(i);
            }
            if (element == null)
            {
                array.arraySize++;
                element = array.GetArrayElementAtIndex(array.arraySize - 1);
            }

            element.FindPropertyRelative("Key").stringValue = EffectKey;
            element.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            element.FindPropertyRelative("SeKey").stringValue = "explosion";
            element.FindPropertyRelative("Lifetime").floatValue = 0f;
            element.FindPropertyRelative("Prewarm").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();

            // プロジェクトのライブラリがサンプル専用のもの自身なら includes は不要
            var projectLibrary = settings.Effects.Library;
            if (projectLibrary == null || projectLibrary == sampleLibrary) return;

            var projectSo = new SerializedObject(projectLibrary);
            var includes = projectSo.FindProperty("includes");
            for (var i = 0; i < includes.arraySize; i++)
            {
                if (includes.GetArrayElementAtIndex(i).objectReferenceValue == sampleLibrary) return;
            }
            includes.arraySize++;
            includes.GetArrayElementAtIndex(includes.arraySize - 1).objectReferenceValue = sampleLibrary;
            projectSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- Scenes

        private static void CreateTitleScene(string path, string gameScenePath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var canvas = AbubuUiMenu.FindOrCreateCanvas();

            var controller = new GameObject("SampleTitle").AddComponent<SampleTitleController>();
            var status = CreateText(canvas.transform, "Status", "", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 60f), 28);
            CreateText(canvas.transform, "Title", "Abubu Base Package Sample", new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(900f, 60f), 36);

            var buttons = CreateButtonColumn(canvas.transform, new Vector2(0f, 0.5f), new Vector2(200f, 0f));
            AddButton(buttons, "BGM A", controller.PlayBgmA);
            AddButton(buttons, "BGM B", controller.PlayBgmB);
            AddButton(buttons, "BGM 停止", controller.StopBgm);
            AddButton(buttons, "SE: jump", controller.PlayJump);
            AddButton(buttons, "SE: coin", controller.PlayCoin);
            AddButton(buttons, "SE: coin x40 連打", controller.PlayCoinBurst);
            AddButton(buttons, "環境音: 風", controller.ToggleWind);
            AddButton(buttons, "環境音: 雨", controller.ToggleRain);
            AddButton(buttons, "エフェクト", controller.PlayEffect);
            AddButton(buttons, "音量設定", controller.ToggleVolumePanel);
            AddButton(buttons, "画面設定", controller.ToggleGraphicsPanel);
            AddButton(buttons, "ゲームへ (データ渡し)", controller.GoToGame);

            var volume = AbubuUiMenu.BuildVolumeSettingsPanel(canvas);
            var graphics = AbubuUiMenu.BuildGraphicsSettingsPanel(canvas);
            MoveRight(volume, new Vector2(-280f, 120f));
            MoveRight(graphics, new Vector2(-320f, -260f));
            volume.SetActive(false);
            graphics.SetActive(false);

            var so = new SerializedObject(controller);
            so.FindProperty("status").objectReferenceValue = status;
            so.FindProperty("volumePanel").objectReferenceValue = volume;
            so.FindProperty("graphicsPanel").objectReferenceValue = graphics;
            so.FindProperty("gameScenePath").stringValue = gameScenePath;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, path);
        }

        private static void CreateGameScene(string path, string titleScenePath, GameObject cubePrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -1f, 0f);

            var cam = Camera.main;
            if (cam != null) cam.transform.SetPositionAndRotation(new Vector3(0f, 3f, -10f), Quaternion.Euler(12f, 0f, 0f));

            var canvas = AbubuUiMenu.FindOrCreateCanvas();
            var controller = new GameObject("SampleGame").AddComponent<SampleGameController>();
            var info = CreateText(canvas.transform, "Info", "", new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 120f), 28);

            var buttons = CreateButtonColumn(canvas.transform, new Vector2(0f, 0.5f), new Vector2(200f, 0f));
            AddButton(buttons, "キューブ x10 (プール)", controller.SpawnCubes);
            AddButton(buttons, "スコア +1 (セーブ/イベント)", controller.AddScore);
            AddButton(buttons, "スコアリセット", controller.ResetScore);
            AddButton(buttons, "リロード", controller.Reload);
            AddButton(buttons, "タイトルへ", controller.BackToTitle);

            var so = new SerializedObject(controller);
            so.FindProperty("info").objectReferenceValue = info;
            so.FindProperty("cubePrefab").objectReferenceValue = cubePrefab;
            so.FindProperty("titleScenePath").stringValue = titleScenePath;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, path);
        }

        private static Text CreateText(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, int fontSize)
        {
            var go = DefaultControls.CreateText(AbubuUiMenu.GetResources());
            go.name = name;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            go.AddComponent<Outline>();
            return t;
        }

        private static Transform CreateButtonColumn(Transform parent, Vector2 anchor, Vector2 position)
        {
            var go = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(320f, 820f);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            return go.transform;
        }

        private static void AddButton(Transform parent, string label, UnityAction action)
        {
            var go = DefaultControls.CreateButton(AbubuUiMenu.GetResources());
            go.name = label;
            go.transform.SetParent(parent, false);
            go.GetComponentInChildren<Text>().text = label;
            go.GetComponentInChildren<Text>().fontSize = 22;
            go.AddComponent<LayoutElement>().preferredHeight = 56f;
            UnityEventTools.AddPersistentListener(go.GetComponent<Button>().onClick, action);
        }

        private static void MoveRight(GameObject panel, Vector2 position)
        {
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
        }

        private static void AddToBuildSettings(params string[] paths)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var path in paths)
            {
                if (scenes.Any(s => s.path == path)) continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------------------------------------------------------------- Utils

        /// <summary>
        /// 生成先フォルダ。サンプルを Import した場所 (このスクリプトの 1 つ上の Basic Demo フォルダ) の Generated に置く。
        /// 特定できない場合や Assets の外 (読み取り専用) の場合は <see cref="FallbackFolder"/>。
        /// </summary>
        private static string ResolveFolder()
        {
            var script = AssetDatabase.FindAssets(nameof(AbubuSampleGenerator) + " t:Script")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == nameof(AbubuSampleGenerator));
            if (string.IsNullOrEmpty(script) || !script.StartsWith("Assets/", StringComparison.Ordinal)) return FallbackFolder;

            var editorFolder = Path.GetDirectoryName(script)!.Replace('\\', '/');
            var sampleRoot = Path.GetDirectoryName(editorFolder)!.Replace('\\', '/');
            return sampleRoot + "/Generated";
        }

        /// <summary>
        /// 実行時に使われる AbubuSettings を探す。Resources/ProjectContext プレハブの AbubuInstaller に設定されたものを優先し、
        /// 見つからなければプロジェクト内に AbubuSettings が 1 つだけある場合にそれを使う。
        /// </summary>
        private static AbubuSettings FindSettings()
        {
            var context = Resources.Load<ProjectContext>(ProjectContext.ProjectContextResourcePath);
            var installer = context != null ? context.GetComponentInChildren<AbubuInstaller>(true) : null;
            if (installer != null)
            {
                // settings は private なシリアライズフィールドなので SerializedObject 経由で読む
                using var serialized = new SerializedObject(installer);
                if (serialized.FindProperty("settings")?.objectReferenceValue is AbubuSettings fromContext) return fromContext;
            }

            var guids = AssetDatabase.FindAssets("t:" + nameof(AbubuSettings));
            return guids.Length == 1 ? AssetDatabase.LoadAssetAtPath<AbubuSettings>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        /// <summary>
        /// AbubuSettings の Sound / Effect ライブラリが空ならサンプルのフォルダに作って割り当てる
        /// (プロジェクトに既にあるライブラリはそのまま使う)。
        /// </summary>
        private static void EnsureLibraries(AbubuSettings settings)
        {
            var changed = false;
            if (settings.Sound.Library == null)
            {
                settings.Sound.Library = LoadOrCreate<SoundLibrary>(SampleSoundLibraryPath);
                changed = true;
            }
            if (settings.Effects.Library == null)
            {
                settings.Effects.Library = LoadOrCreate<EffectLibrary>(SampleEffectLibraryPath);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(settings);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
