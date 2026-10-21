using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

// Komut satırından çalıştırılır:
//   Unity -batchmode -buildTarget Android -executeMethod GecitSetup.Setup
//   Unity -batchmode -buildTarget Android -executeMethod GecitSetup.Build
public static class GecitSetup
{
    const string ScenePath = "Assets/Scenes/Gecit.unity";
    const string ApkPath = "Builds/GECIT.apk";

    public static void Setup()
    {
        // MediaPipe için Android'de gereken libc++_shared.so (sadece ARM64)
        const string libPath = "Assets/Plugins/Android/libs/arm64-v8a/libc++_shared.so";
        if (File.Exists(libPath))
        {
            AssetDatabase.ImportAsset(libPath);
            var plugin = (PluginImporter)AssetImporter.GetAtPath(libPath);
            plugin.SetCompatibleWithAnyPlatform(false);
            plugin.SetCompatibleWithEditor(false);
            plugin.SetCompatibleWithPlatform(BuildTarget.Android, true);
            plugin.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
            plugin.SaveAndReimport();
        }

        // TextMeshPro temel dosyaları (tarayıcı paneli kullanıyor)
        if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
        {
            AssetDatabase.ImportPackage("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage", false);
            AssetDatabase.Refresh();
            Debug.Log("TMP temel dosyalari kuruldu: " + AssetDatabase.IsValidFolder("Assets/TextMesh Pro"));
        }

        ConfigurePlayer();
        ConfigureXR();
        CreateScene();
        AssetDatabase.SaveAssets();
        Debug.Log("GECIT SETUP OK");
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "htmert10";
        PlayerSettings.productName = "GECIT";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.htmert10.gecit");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
    }

    static void ConfigureXR()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        general.InitManagerOnStart = true;
        bool ok = XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android);
        Debug.Log("ARCore loader atandi: " + ok);

        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
    }

    static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        new GameObject("Uygulama Ayarlari", typeof(AppSetup));
        new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
        new GameObject("Tarayici Yonetici", typeof(TLab.WebView.BrowserManager));

        // XR Origin > Camera Offset > AR Camera (takip + oda görüntüsü) > Sol/Sağ göz
        var originGO = new GameObject("XR Origin", typeof(XROrigin));
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(originGO.transform, false);

        var camGO = new GameObject("AR Camera", typeof(Camera), typeof(ARCameraManager), typeof(ARCameraBackground));
        camGO.tag = "MainCamera";
        camGO.transform.SetParent(offset.transform, false);
        var arCam = camGO.GetComponent<Camera>();
        arCam.clearFlags = CameraClearFlags.SolidColor;
        arCam.backgroundColor = Color.black;
        arCam.cullingMask = 0;
        arCam.depth = -10;
        arCam.nearClipPlane = 0.05f;
        arCam.farClipPlane = 30f;
        camGO.AddComponent<ARPoseUpdater>();
        camGO.AddComponent<CameraConfigPicker>().cameraManager = camGO.GetComponent<ARCameraManager>();   // 60 fps varsa seç
        var occlusionManager = camGO.AddComponent<AROcclusionManager>();   // derinlik: yalnız oda taramasında açılır
        occlusionManager.enabled = false;

        var origin = originGO.GetComponent<XROrigin>();
        origin.Camera = arCam;
        origin.CameraFloorOffsetObject = offset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
        origin.CameraYOffset = 0f;

        var leftEye = CreateEye(camGO.transform, "Sol Goz", -0.032f, new Rect(0f, 0f, 0.5f, 1f), Color.black);
        var rightEye = CreateEye(camGO.transform, "Sag Goz", 0.032f, new Rect(0.5f, 0f, 0.5f, 1f), Color.black);
        leftEye.cullingMask = ~(1 << 9);
        rightEye.cullingMask = ~(1 << 8);

        // Arka planda oda: her gözün arkasında kamera görüntüsünü gösteren perde
        var pass = camGO.AddComponent<PassthroughView>();
        pass.arCamera = arCam;
        pass.leftEye = leftEye;
        pass.rightEye = rightEye;
        pass.leftScreen = CreatePassthroughScreen(leftEye.transform, "Sol Perde", 8);
        pass.rightScreen = CreatePassthroughScreen(rightEye.transform, "Sag Perde", 9);

        var light = new GameObject("Isik", typeof(Light));
        light.GetComponent<Light>().type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

        // ---- Pencereler (Meta tarzı, yan yana, hafif kavisli) ----
        var rounded = CreateCircleSprite("Yuvarlak", 128, 40f, false);
        var ringSprite = CreateCircleSprite("Halka", 128, 64f, true);
        var dotSprite = CreateCircleSprite("Nokta", 128, 64f, false);
        var backMat = SaveMaterial(new Material(Shader.Find("Unlit/Transparent Cutout")) { mainTexture = CreateBackTexture() }, "Pencere Arka");

        var anchorManager = originGO.AddComponent<ARAnchorManager>();
        var pointCloudManager = originGO.AddComponent<ARPointCloudManager>();
        var quality = originGO.AddComponent<TrackingQuality>();
        quality.pointClouds = pointCloudManager;

        var youtube = CreateWindow("YouTube", "https://m.youtube.com", new Vector3(-1.1f, 0.05f, 2.1f),
            camGO.transform, leftEye, rounded, ringSprite, backMat, anchorManager, out var youtubeGroup);
        var google = CreateWindow("Google", "https://www.google.com", new Vector3(1.1f, 0.05f, 2.1f),
            camGO.transform, leftEye, rounded, ringSprite, backMat, anchorManager, out var googleGroup);
        quality.windowGroups = new[] { youtubeGroup, googleGroup };

        // Yedek pencereler (Uygulamalar menüsünden yeni site açılınca kullanılır); kapalı başlar,
        // açılana kadar tarayıcıları çalışmaz
        var spare1 = CreateWindow("Yedek 1", "https://www.google.com", new Vector3(0f, 0.05f, 1.4f),
            camGO.transform, leftEye, rounded, ringSprite, backMat, anchorManager, out _);
        var spare2 = CreateWindow("Yedek 2", "https://www.google.com", new Vector3(0f, 0.05f, 1.4f),
            camGO.transform, leftEye, rounded, ringSprite, backMat, anchorManager, out _);
        // Açılışta hiçbir pencere açık değil (kullanıcı: menüden açılsın); hepsi havuzda bekler
        youtube.gameObject.SetActive(false);
        google.gameObject.SetActive(false);
        spare1.gameObject.SetActive(false);
        spare2.gameObject.SetActive(false);
        var pool = new GameObject("Pencere Havuzu").AddComponent<WindowPool>();
        pool.head = camGO.transform;
        pool.spares = new[] { youtube, google, spare1, spare2 };

        // Sinema modu (sayfadaki betik "Sinema" nesnesine mesaj gönderir: adı değişmemeli)
        var cinema = new GameObject("Sinema").AddComponent<CinemaMode>();
        cinema.head = camGO.transform;
        cinema.dimMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Dim")) { color = new Color(0f, 0f, 0f, 0f) }, "Sinema Karartma");

        // Saat / hava paneli
        new GameObject("Saat Paneli Yonetici").AddComponent<InfoPanel>().head = camGO.transform;

        // Merkez işareti (poster): tanınınca pencerelerin yerini ona göre sabitler/hatırlar
        var markerTex = CreateMarkerTexture();
        var library = ScriptableObject.CreateInstance<UnityEngine.XR.ARSubsystems.XRReferenceImageLibrary>();
        if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
        AssetDatabase.CreateAsset(library, "Assets/XR/IsaretKutuphanesi.asset");
        UnityEditor.XR.ARSubsystems.XRReferenceImageLibraryExtensions.Add(library);
        UnityEditor.XR.ARSubsystems.XRReferenceImageLibraryExtensions.SetTexture(library, 0, markerTex, false);
        UnityEditor.XR.ARSubsystems.XRReferenceImageLibraryExtensions.SetName(library, 0, "GecitIsaret");
        UnityEditor.XR.ARSubsystems.XRReferenceImageLibraryExtensions.SetSpecifySize(library, 0, true);
        UnityEditor.XR.ARSubsystems.XRReferenceImageLibraryExtensions.SetSize(library, 0, new Vector2(0.19f, 0.19f));
        EditorUtility.SetDirty(library);

        var imageManager = originGO.AddComponent<ARTrackedImageManager>();
        var imSo = new SerializedObject(imageManager);
        imSo.FindProperty("m_SerializedLibrary").objectReferenceValue = library;
        imSo.ApplyModifiedPropertiesWithoutUndo();
        imageManager.requestedMaxNumberOfMovingImages = 1;
        imageManager.enabled = false;   // merkez işareti kullanılmıyor: ARCore'a boşuna görüntü arattırma
        var markerSpace = originGO.AddComponent<MarkerSpace>();
        markerSpace.imageManager = imageManager;


        // Halka imleç ve elden çıkan ışın (tek merkezden)
        var cursorGO = new GameObject("Imlec", typeof(SpriteRenderer));
        var cursorSr = cursorGO.GetComponent<SpriteRenderer>();
        var cursorDot = CreateCursorSprite();
        cursorSr.sprite = cursorDot;
        cursorSr.color = new Color(1f, 1f, 1f, 0.95f);
        cursorSr.sortingOrder = 10;
        cursorSr.sharedMaterial = SaveMaterial(new Material(Shader.Find("Gecit/CursorInvert")), "Imlec");   // arka plana göre renk değiştirir, hep üstte

        var lineGO = new GameObject("Isin", typeof(MeshFilter), typeof(MeshRenderer));
        var line = lineGO.AddComponent<RayBeam>();   // elden çıkar, imlece varır; uçlarda soluk, ortada belirgin
        line.view = camGO.transform;
        lineGO.GetComponent<MeshRenderer>().sharedMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Ray")) { mainTexture = CreateBeamTexture() }, "Isin");

        var pointer = new GameObject("Isin Yoneticisi").AddComponent<PointerManager>();
        pointer.head = camGO.transform;
        pointer.cursor = cursorSr;
        pointer.ringSprite = cursorDot;
        pointer.dotSprite = cursorDot;
        pointer.line = line;
        pointer.ringMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Overlay")), "Imlec Halka");

        // İkinci elin işaretçisi: ayrı nokta + ayrı çizgi (iki elle kullanım)
        var cursor2 = Object.Instantiate(cursorGO);
        cursor2.name = "Imlec 2";
        var line2GO = new GameObject("Isin 2", typeof(MeshFilter), typeof(MeshRenderer));
        var line2 = line2GO.AddComponent<RayBeam>();
        line2.view = camGO.transform;
        line2GO.GetComponent<MeshRenderer>().sharedMaterial = lineGO.GetComponent<MeshRenderer>().sharedMaterial;
        var pointer2 = new GameObject("Isin Yoneticisi 2").AddComponent<PointerManager>();
        pointer2.secondary = true;
        pointer2.head = camGO.transform;
        pointer2.cursor = cursor2.GetComponent<SpriteRenderer>();
        pointer2.ringSprite = cursorDot;
        pointer2.dotSprite = cursorDot;
        pointer2.line = line2;
        pointer2.ringMaterial = pointer.ringMaterial;

        // Gözlük ayarı paneli (göz arası)
        new GameObject("Gozluk Ayari Yonetici").AddComponent<GogglesPanel>().head = camGO.transform;

        // Sinema salonu (menü: "Sinema")
        var cinemaHall = new GameObject("Sinema Salonu").AddComponent<CinemaHall>();
        cinemaHall.head = camGO.transform;
        cinemaHall.screenMaterial = SaveMaterial(new Material(Shader.Find("Gecit/CurvedScreen")), "Kavisli Perde");

        // Oda tarama (menü: "Oda tara")
        var scan = new GameObject("Oda Tarama").AddComponent<RoomScan>();
        scan.cameraManager = camGO.GetComponent<ARCameraManager>();
        scan.occlusion = occlusionManager;
        scan.arCamera = arCam;
        scan.head = camGO.transform;
        scan.voxelMaterial = SaveMaterial(new Material(Shader.Find("Gecit/ScanVoxel")) { enableInstancing = true }, "Tarama Blogu");

        camGO.AddComponent<GazeTouchSource>().head = camGO.transform;

        // El takibi + Meta tarzı el iskeleti
        var hand = camGO.AddComponent<HandTracker>();
        hand.cameraManager = camGO.GetComponent<ARCameraManager>();
        hand.arCamera = arCam;
        hand.head = camGO.transform;
        var handVisual = new GameObject("Eldiven El").AddComponent<HandVisual>();
        handVisual.gloveMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Glove")), "Eldiven");
        handVisual.shellMaterial = null;   // eli pencerenin önüne çıkarma kapalı (tek kamerada düzgün olmuyor)
        // El: tamamını saran halka + halkanın tepesinden dik çubuk. Tıklama kumandadan (OK) ya da el modunda
        // baş+işaret birleşince (HandTracker.HandClicks, kumanda 4 / menü). Eski el görseli kapalı.
        handVisual.gameObject.SetActive(false);
        var ringGO = new GameObject("El Halkasi", typeof(LineRenderer), typeof(HandRing));
        ringGO.GetComponent<LineRenderer>().material = SaveMaterial(new Material(Shader.Find("Gecit/PerdeOverlay")), "El Halkasi");
        var ring = ringGO.GetComponent<HandRing>();
        ring.arCamera = arCam;
        ring.tracker = hand;

        // Çalışırken kurulan arayüz: ortak görseller, VR klavye, ana menü, ışın kılıcı oyunu
        var uiAssets = new GameObject("Arayuz Gorselleri").AddComponent<UiAssets>();
        uiAssets.rounded = rounded;
        uiAssets.holo = SaveMaterial(new Material(Shader.Find("Gecit/HoloFrame")), "Holo Cerceve");
        uiAssets.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Inter.ttf");
        new GameObject("Klavye").AddComponent<VRKeyboard>().head = camGO.transform;

        var game = new GameObject("Isin Kilici Oyunu").AddComponent<SaberGame>();
        game.head = camGO.transform;
        game.coreMaterial = SaveMaterial(new Material(Shader.Find("Unlit/Color")) { color = new Color(0.92f, 0.98f, 1f) }, "Kilic Oz");
        game.glowMaterial = SaveMaterial(new Material(Shader.Find("Sprites/Default")) { color = new Color(0.25f, 0.75f, 1f, 0.45f) }, "Kilic Parilti");
        game.sparkMaterial = SaveMaterial(new Material(Shader.Find("Unlit/Color")) { color = new Color(0.75f, 0.92f, 1f) }, "Kivilcim");
        game.cubeMaterials = new[]
        {
            SaveMaterial(new Material(Shader.Find("Gecit/Block")) { color = new Color(1f, 0.22f, 0.32f) }, "Kup Kirmizi"),
            SaveMaterial(new Material(Shader.Find("Gecit/Block")) { color = new Color(0.2f, 0.55f, 1f) }, "Kup Mavi"),
        };

        new GameObject("Kumanda").AddComponent<GecitController>();   // Arduino + HC-06 Bluetooth kumanda

        // Avuç menüsü: avuç sana dönükken logo belirir, baş+işaret birleşince menü açılır (kumandasız)
        var palmMenu = new GameObject("Avuc Menusu").AddComponent<PalmMenu>();
        palmMenu.arCamera = arCam;
        palmMenu.material = SaveMaterial(new Material(Shader.Find("Gecit/Overlay")), "Avuc Logo");

        // Sanal dünya modu (oda yerine gece göğü, dağlar, ışıklı zemin)
        var vw = new GameObject("Sanal Dunya Yonetici").AddComponent<VirtualWorld>();
        vw.head = camGO.transform;
        vw.passthrough = pass;
        vw.skyMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Sky")), "Gokyuzu");
        var floorMat = new Material(Shader.Find("Gecit/Floor"));
        floorMat.SetColor("_Base", new Color(0.30f, 0.21f, 0.15f));   // gece: fener ışığında ahşap
        floorMat.SetColor("_Line", new Color(0.12f, 0.08f, 0.06f));
        vw.floorMaterial = SaveMaterial(floorMat, "Sanal Zemin");
        var terrainMat = new Material(Shader.Find("Gecit/VertexColor"));
        terrainMat.SetColor("_Fog", new Color(0.06f, 0.08f, 0.14f));   // gece pusu
        terrainMat.SetFloat("_FogStart", 30f);
        terrainMat.SetFloat("_FogEnd", 900f);
        vw.mountainMaterial = SaveMaterial(terrainMat, "Daglar");
        vw.orbMaterial = SaveMaterial(new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.92f, 0.5f) }, "Atesbocegi");
        var gameMat = new Material(Shader.Find("Gecit/VirtualHand"));
        gameMat.SetFloat("_UseFade", 0f);
        gameMat.SetColor("_Color", new Color(0.8f, 0.82f, 0.88f, 1f));
        gameMat.SetColor("_RimColor", new Color(1f, 1f, 1f, 1f));
        vw.gameMaterial = SaveMaterial(gameMat, "Oyun Nesnesi");
        vw.saber = game;
        var balloonGame = new GameObject("Balon Oyunu").AddComponent<BalloonGame>();
        balloonGame.head = camGO.transform;
        balloonGame.material = gameMat;
        vw.balloon = balloonGame;
        var targetGame = new GameObject("Hedef Oyunu").AddComponent<TargetGame>();
        targetGame.head = camGO.transform;
        targetGame.material = gameMat;
        vw.target = targetGame;
        var whackGame = new GameObject("Kostebek Oyunu").AddComponent<WhackGame>();
        whackGame.head = camGO.transform;
        whackGame.material = gameMat;
        vw.whack = whackGame;
        var basketGame = new GameObject("Basket Oyunu").AddComponent<BasketGame>();
        basketGame.head = camGO.transform;
        basketGame.material = gameMat;
        vw.basket = basketGame;
        vw.skyDay = SaveMaterial(new Material(Shader.Find("Gecit/SkyDay")), "Gokyuzu Gunduz");
        vw.skySpace = SaveMaterial(new Material(Shader.Find("Gecit/SkySpace")), "Gokyuzu Uzay");
        vw.skySunset = SaveMaterial(new Material(Shader.Find("Gecit/SkySunset")), "Gokyuzu Gunbatimi");
        vw.seaMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Sea")), "Deniz");
        vw.planetMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Planet")), "Gezegen");
        vw.panoMaterial = SaveMaterial(new Material(Shader.Find("Gecit/PanoSky")), "Panorama");
        vw.holoMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Hologram")), "Hologram");
        vw.cinemaSky = SaveMaterial(new Material(Shader.Find("Gecit/SkyCinema")), "Sinema Gokyuzu");
        vw.panoAvailable = ImportPanoramas();
        ImportAmbientSounds();
        vw.fadeMaterial = SaveMaterial(new Material(Shader.Find("Gecit/Overlay")) { color = new Color(0f, 0f, 0f, 0f) }, "Karartma");
        RenderSettings.skybox = vw.skyMaterial;

        // Sanal dünyada Quest tarzı gri el modeli (WebXR generic-hand, MIT); model yoksa çizilmiş eldiven
        var leftHandModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/El/left.glb");
        var rightHandModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/El/right.glb");
        if (leftHandModel != null && rightHandModel != null)
        {
            var mhand = new GameObject("Sanal El").AddComponent<ModelHand>();
            mhand.leftModel = leftHandModel;
            mhand.rightModel = rightHandModel;
            mhand.material = SaveMaterial(new Material(Shader.Find("Gecit/QuestHand")), "Quest El");
        }
        else
        {
            var vhand = new GameObject("Sanal El").AddComponent<VirtualHand>();
            vhand.material = SaveMaterial(new Material(Shader.Find("Gecit/VirtualHand")), "Sanal El");
        }
        // Kumanda modunda elin yerinde Quest tarzı kumanda modeli
        var vctrl = new GameObject("Sanal Kumanda").AddComponent<VirtualController>();
        vctrl.material = gameMat;
        vctrl.arCamera = arCam;

        var menu = new GameObject("Ana Menu").AddComponent<HomeMenu>();
        menu.head = camGO.transform;
        menu.game = game;

        // Ekran üstü durum yazısı
        var canvasGO = new GameObject("Ekran Ustu", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        // Fotoğraf çekince kısa beyaz parlama
        var flashGO = new GameObject("Foto Parlamasi", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        flashGO.transform.SetParent(canvasGO.transform, false);
        var frt2 = flashGO.GetComponent<RectTransform>();
        frt2.anchorMin = Vector2.zero; frt2.anchorMax = Vector2.one; frt2.offsetMin = frt2.offsetMax = Vector2.zero;
        flashGO.GetComponent<Image>().color = Color.white;
        flashGO.GetComponent<Image>().raycastTarget = false;
        var flashGroup = flashGO.GetComponent<CanvasGroup>();
        flashGroup.alpha = 0f;
        flashGroup.blocksRaycasts = false;
        canvasGO.AddComponent<Screenshot>().flash = flashGroup;

        var label = CreateText(canvasGO.transform, "Durum Yazisi", 30, new Color(1f, 0.85f, 0.4f, 0.95f));
        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 1f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.sizeDelta = new Vector2(0f, 50f);
        var hud = canvasGO.AddComponent<DebugHUD>();
        hud.label = label;
        hud.arCamera = camGO.transform;
        hud.cameraManager = camGO.GetComponent<ARCameraManager>();
        hud.eye = leftEye;

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    static Renderer CreatePassthroughScreen(Transform eye, string name, int layer)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(eye, false);
        quad.layer = layer;
        var r = quad.GetComponent<Renderer>();
        r.sharedMaterial = SaveMaterial(new Material(Shader.Find("Unlit/Texture")), name);
        return r;
    }

    // Tek bir Meta tarzı pencere: koyu cam çerçeve, yuvarlak köşeli içerik, gri arka yüz,
    // altta hap çubuk, sağ alt köşede boyut tutamacı. Kullanıcıya dönük yerleşir.
    static Window CreateWindow(string name, string url, Vector3 position, Transform head, Camera eye,
        Sprite rounded, Sprite ringSprite, Material backMat, ARAnchorManager anchorManager, out CanvasGroup group)
    {
        var windowRoot = new GameObject("Pencere " + name);
        windowRoot.transform.position = position;
        windowRoot.transform.rotation = Quaternion.LookRotation(new Vector3(position.x, 0f, position.z), Vector3.up);

        var canvasGO = new GameObject("Tuval", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGO.transform.SetParent(windowRoot.transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eye;
        var crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(1280f, 720f);
        crt.localScale = Vector3.one * 0.0012f;
        crt.localPosition = Vector3.zero;
        group = canvasGO.GetComponent<CanvasGroup>();

        // Arka yüz: arkadan bakınca gri görünür, içeriği gizler
        var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.name = "Arka Yuz";
        Object.DestroyImmediate(back.GetComponent<Collider>());
        back.transform.SetParent(windowRoot.transform, false);
        back.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        back.transform.localScale = new Vector3(1.57f, 0.898f, 1f);
        back.GetComponent<Renderer>().sharedMaterial = backMat;

        var frame = new GameObject("Cerceve", typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(canvasGO.transform, false);
        var frt = frame.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(-14f, -14f);
        frt.offsetMax = new Vector2(14f, 14f);
        var frameImg = frame.GetComponent<Image>();
        frameImg.sprite = rounded;
        frameImg.type = Image.Type.Sliced;
        frameImg.color = new Color(0.08f, 0.09f, 0.12f, 0.88f);
        frame.transform.localPosition = new Vector3(0f, 0f, 2f);

        var maskGO = new GameObject("Icerik", typeof(RectTransform), typeof(Image), typeof(Mask));
        maskGO.transform.SetParent(canvasGO.transform, false);
        var mrt = maskGO.GetComponent<RectTransform>();
        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;
        var maskImg = maskGO.GetComponent<Image>();
        maskImg.sprite = rounded;
        maskImg.type = Image.Type.Sliced;
        maskImg.pixelsPerUnitMultiplier = 1.4f;
        maskGO.GetComponent<Mask>().showMaskGraphic = false;

        var browserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.tlabaltoh.webview/Resources/TLab/WebView/Browser.prefab");
        var browserGO = (GameObject)PrefabUtility.InstantiatePrefab(browserPrefab);
        browserGO.transform.SetParent(maskGO.transform, false);
        var brt = browserGO.GetComponent<RectTransform>();
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero;
        brt.offsetMax = Vector2.zero;
        brt.localScale = Vector3.one;
        brt.localRotation = Quaternion.identity;

        foreach (var c in browserGO.GetComponents<Component>())
            if (c is CanvasScaler || c is GraphicRaycaster) Object.DestroyImmediate(c);
        var nested = browserGO.GetComponent<Canvas>();
        if (nested != null) Object.DestroyImmediate(nested);
        foreach (var c in browserGO.GetComponents<MonoBehaviour>())
            if (c.GetType().Name.EndsWith("Sample")) Object.DestroyImmediate(c);
        foreach (Transform child in browserGO.transform)
            if (child.name != "View") child.gameObject.SetActive(false);

        var webView = browserGO.GetComponentInChildren<TLab.WebView.WebView>(true);
        var vrt = webView.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.pivot = new Vector2(0.5f, 0.5f);
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;
        vrt.localScale = Vector3.one;
        vrt.localRotation = Quaternion.identity;

        var so = new SerializedObject(webView);
        so.FindProperty("m_url").stringValue = url;
        so.FindProperty("m_viewSize").vector2IntValue = new Vector2Int(1280, 720);
        so.FindProperty("m_texSize").vector2IntValue = new Vector2Int(1280, 720);
        var mode = so.FindProperty("m_captureMode");
        mode.enumValueIndex = System.Array.IndexOf(mode.enumNames, "HardwareBuffer");
        so.ApplyModifiedPropertiesWithoutUndo();

        var container = browserGO.GetComponentInChildren<TLab.WebView.BrowserContainer>(true);
        var vrBrowser = canvasGO.AddComponent<VRBrowser>();
        vrBrowser.container = container;
        vrBrowser.view = webView.GetComponent<RawImage>();
        vrBrowser.windowName = name;

        var barGO = new GameObject("Tasima Cubugu", typeof(RectTransform), typeof(Image), typeof(BoxCollider));
        barGO.transform.SetParent(canvasGO.transform, false);
        var barRt = barGO.GetComponent<RectTransform>();
        barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0f);
        barRt.pivot = new Vector2(0.5f, 0.5f);
        barRt.anchoredPosition = new Vector2(0f, -44f);
        barRt.sizeDelta = new Vector2(260f, 18f);
        var barImg = barGO.GetComponent<Image>();
        barImg.sprite = rounded;
        barImg.type = Image.Type.Sliced;
        barImg.pixelsPerUnitMultiplier = 4.4f;
        barImg.color = new Color(0.92f, 0.93f, 0.97f, 0.45f);
        var barCol = barGO.GetComponent<BoxCollider>();
        barCol.size = new Vector3(320f, 70f, 20f);

        var resizeGO = new GameObject("Boyut Tutamaci", typeof(RectTransform), typeof(Image), typeof(BoxCollider));
        resizeGO.transform.SetParent(canvasGO.transform, false);
        var rzRt = resizeGO.GetComponent<RectTransform>();
        rzRt.anchorMin = rzRt.anchorMax = new Vector2(1f, 0f);
        rzRt.pivot = new Vector2(0.5f, 0.5f);
        rzRt.anchoredPosition = new Vector2(30f, -30f);
        rzRt.sizeDelta = new Vector2(44f, 44f);
        var rzImg = resizeGO.GetComponent<Image>();
        rzImg.sprite = ringSprite;
        rzImg.color = new Color(1f, 1f, 1f, 0f);
        resizeGO.GetComponent<BoxCollider>().size = new Vector3(110f, 110f, 20f);
        // Sol alt köşede ikinci boyut tutamacı (kullanıcı: sol alttan tutmak istiyor)
        var resize2GO = Object.Instantiate(resizeGO, canvasGO.transform);
        resize2GO.name = "Boyut Tutamaci Sol";
        var rz2Rt = resize2GO.GetComponent<RectTransform>();
        rz2Rt.anchorMin = rz2Rt.anchorMax = new Vector2(0f, 0f);
        rz2Rt.anchoredPosition = new Vector2(-30f, -30f);

        var windowAnchor = windowRoot.AddComponent<WindowAnchor>();
        windowAnchor.anchorManager = anchorManager;

        var window = windowRoot.AddComponent<Window>();
        window.head = head;
        window.content = vrt;
        window.container = container;
        window.moveBar = barCol;
        window.resizeHandle = resizeGO.GetComponent<BoxCollider>();
        window.barImage = barImg;
        window.resizeImage = rzImg;
        window.resizeHandle2 = resize2GO.GetComponent<BoxCollider>();
        window.resizeImage2 = resize2GO.GetComponent<Image>();
        window.anchor = windowAnchor;
        return window;
    }

    // Bol desenli (ARCore'un kolay tanıyacağı) merkez işareti; masaüstüne de basılabilir kopyası konur
    static Texture2D CreateMarkerTexture()
    {
        const string path = "Assets/Textures/GecitIsaret.png";
        Directory.CreateDirectory("Assets/Textures");
        const int size = 1024;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        var rng = new System.Random(2026);
        Color32[] palette =
        {
            new Color32(15, 15, 20, 255), new Color32(0, 150, 220, 255), new Color32(230, 70, 40, 255),
            new Color32(250, 190, 20, 255), new Color32(40, 170, 90, 255), new Color32(120, 60, 200, 255),
        };
        void Rect(int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(size, y0 + h); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(size, x0 + w); x++)
                    pixels[y * size + x] = c;
        }
        void Circle(int cx, int cy, int r, Color32 c)
        {
            for (int y = Mathf.Max(0, cy - r); y < Mathf.Min(size, cy + r); y++)
                for (int x = Mathf.Max(0, cx - r); x < Mathf.Min(size, cx + r); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) pixels[y * size + x] = c;
        }
        for (int i = 0; i < 160; i++)
        {
            var c = palette[rng.Next(palette.Length)];
            int x = rng.Next(size), y = rng.Next(size);
            if (rng.Next(2) == 0) Rect(x, y, 20 + rng.Next(140), 20 + rng.Next(140), c);
            else Circle(x, y, 10 + rng.Next(70), c);
        }
        // Kalın siyah kenar
        Rect(0, 0, size, 28, palette[0]); Rect(0, size - 28, size, 28, palette[0]);
        Rect(0, 0, 28, size, palette[0]); Rect(size - 28, 0, 28, size, palette[0]);
        tex.SetPixels32(pixels);
        var png = tex.EncodeToPNG();
        File.WriteAllBytes(path, png);
        try { File.WriteAllBytes(@"C:\Users\halil\OneDrive\Desktop\gecit\GECIT_Isaret.png", png); } catch { }
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Pencere arka yüzü için 16:9 yuvarlak köşeli gri doku
    static Texture2D CreateBackTexture()
    {
        const string path = "Assets/Textures/PencereArka.png";
        Directory.CreateDirectory("Assets/Textures");
        const int w = 512, h = 292;
        const float r = 26f;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var gray = new Color(0.30f, 0.31f, 0.34f, 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - w * 0.5f) - (w * 0.5f - r));
                float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - h * 0.5f) - (h * 0.5f - r));
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(gray.r, gray.g, gray.b, a));
            }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Yuvarlak köşeli kare (dilimlenebilir) ya da halka/nokta sprite'ı üretir
    static Sprite CreateCircleSprite(string name, int size, float radius, bool ring)
    {
        string path = "Assets/Textures/" + name + ".png";
        Directory.CreateDirectory("Assets/Textures");
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float a;
                if (ring || name == "Nokta")
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                    a = ring ? Mathf.Clamp01(1f - Mathf.Abs(r - (half - 12f)) / 5f) : Mathf.Clamp01(half - 6f - r);
                }
                else
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - half) - (half - radius));
                    float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - half) - (half - radius));
                    a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.spritePixelsPerUnit = 100f;
        if (!ring && name != "Nokta") importer.spriteBorder = new Vector4(radius, radius, radius, radius);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Meta tarzı ışın dokusu: genişlik boyunca beyaz parlak çekirdek, dışında ince koyu hale (beyaz sayfada da görünür)
    static Texture2D CreateBeamTexture()
    {
        const string path = "Assets/Textures/Isin Doku.png";
        Directory.CreateDirectory("Assets/Textures");
        const int w = 4, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float d = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;   // 0 orta, 1 kenar
            float core = Mathf.Exp(-d * d * 7f);
            float halo = 0.4f * Mathf.Exp(-d * d * 2f);
            var c = Color.Lerp(new Color(0.55f, 0.6f, 0.68f), Color.white, Mathf.Clamp01(core * 1.4f));   // kenar hafif koyu: açık zeminde de seçilsin
            c.a = Mathf.Clamp01(Mathf.Max(core, halo) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, d))));
            for (int x = 0; x < w; x++) tex.SetPixel(x, y, c);
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.isReadable = false;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // İmleç dokusu (Gecit/CursorInvert): R kanalı = nokta (altındaki rengi ters çevirir),
    // G kanalı = noktanın çevresindeki ince halka (gri zeminde de görünür)
    static Sprite CreateCursorSprite()
    {
        const string path = "Assets/Textures/Imlec Nokta.png";
        Directory.CreateDirectory("Assets/Textures");
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                float dot = Mathf.Clamp01(26f - r);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 30.5f) / 4.5f);
                tex.SetPixel(x, y, new Color(dot, ring, 0f, Mathf.Max(dot, ring)));
            }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;   // ayrık parçalar (nokta + halka) çoklu sanılmasın
        importer.sRGBTexture = false;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Camera CreateEye(Transform head, string name, float x, Rect viewport, Color background)
    {
        var go = new GameObject(name, typeof(Camera));
        go.transform.SetParent(head, false);
        go.transform.localPosition = new Vector3(x, 0f, 0f);
        var cam = go.GetComponent<Camera>();
        cam.rect = viewport;
        cam.fieldOfView = 85f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 100f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.depth = 0;
        return cam;
    }

    static Text CreateText(Transform parent, string name, int size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        return text;
    }

    static Material SaveMaterial(Material mat, string name)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        AssetDatabase.CreateAsset(mat, "Assets/Materials/" + name + ".mat");
        return mat;
    }

    static Texture2D CreateGridTexture()
    {
        const string path = "Assets/Textures/Izgara.png";
        Directory.CreateDirectory("Assets/Textures");
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var bg = new Color(0.03f, 0.05f, 0.12f);
        var line = new Color(0f, 0.75f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, (x < 3 || y < 3) ? line : bg);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.filterMode = FilterMode.Trilinear;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void CreateCube(string name, Vector3 position, Color color)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * 0.3f;
        cube.GetComponent<Renderer>().sharedMaterial = SaveMaterial(new Material(Shader.Find("Standard")) { color = color }, name);
        cube.AddComponent<Spin>();
    }

    // Komut satırı modunda Unity'nin içindeki JDK/SDK/NDK/Gradle yollarını açıkça ver.
    static void SetEmbeddedAndroidTools()
    {
        string root = EditorApplication.applicationContentsPath + "/PlaybackEngines/AndroidPlayer";
        var type = System.Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
        if (type == null)
        {
            Debug.LogError("AndroidExternalToolsSettings bulunamadi");
            return;
        }
        void Set(string prop, string path)
        {
            var p = type.GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p != null) { p.SetValue(null, path); Debug.Log(prop + " = " + path); }
            else Debug.LogWarning(prop + " ozelligi yok");
        }
        Set("jdkRootPath", root + "/OpenJDK");
        Set("sdkRootPath", root + "/SDK");
        Set("ndkRootPath", root + "/NDK");
        Set("gradlePath", root + "/Tools/gradle");
    }

    // Sanal dünyayı editörde kurup fotoğrafını çeker (telefona yüklemeden görsel kontrol)
    public static void PreviewWorld()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var vw = new GameObject("Onizleme").AddComponent<VirtualWorld>();
        vw.skyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Gokyuzu.mat");
        vw.floorMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sanal Zemin.mat");
        vw.mountainMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Daglar.mat");
        vw.orbMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Atesbocegi.mat");
        vw.gameMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Oyun Nesnesi.mat");
        RenderSettings.skybox = vw.skyMaterial;
        vw.skyDay = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Gokyuzu Gunduz.mat");
        vw.skySpace = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Gokyuzu Uzay.mat");
        vw.skySunset = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Gokyuzu Gunbatimi.mat");
        vw.seaMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Deniz.mat");
        vw.planetMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Gezegen.mat");
        vw.panoMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Panorama.mat");
        vw.holoMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Hologram.mat");
        for (int e = 0; e < 4; e++) vw.panoAvailable[e] = File.Exists(PanoDir + PanoFiles[e] + ".jpg");
        var envRoots = new Transform[4];
        for (int e = 0; e < 4; e++)
        {
            envRoots[e] = vw.BuildForPreview((VirtualWorld.Env)e);
            envRoots[e].position = new Vector3(0f, -1.25f, 0f);
            envRoots[e].gameObject.SetActive(false);
        }

        var camGO = new GameObject("Kamera", typeof(Camera));
        var cam = camGO.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 62f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1500f;
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        // Sanal el: tipik açık el duruşu (metre; x sağ, y yukarı, z ileri), kameranın önünde
        var hand = new GameObject("Sanal El").AddComponent<ModelHand>();
        hand.leftModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/El/left.glb");
        hand.rightModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/El/right.glb");
        hand.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Quest El.mat");
        var pose = new Vector3[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0.028f, 0.018f, 0.004f), new Vector3(0.048f, 0.038f, 0.008f), new Vector3(0.062f, 0.058f, 0.012f), new Vector3(0.072f, 0.078f, 0.016f),
            new Vector3(0.030f, 0.088f, 0f), new Vector3(0.033f, 0.128f, 0.006f), new Vector3(0.035f, 0.152f, 0.012f), new Vector3(0.036f, 0.172f, 0.018f),
            new Vector3(0.008f, 0.094f, 0f), new Vector3(0.008f, 0.138f, 0.006f), new Vector3(0.008f, 0.166f, 0.012f), new Vector3(0.008f, 0.188f, 0.018f),
            new Vector3(-0.013f, 0.089f, 0f), new Vector3(-0.015f, 0.129f, 0.006f), new Vector3(-0.016f, 0.154f, 0.012f), new Vector3(-0.017f, 0.174f, 0.018f),
            new Vector3(-0.032f, 0.080f, 0f), new Vector3(-0.036f, 0.110f, 0.006f), new Vector3(-0.038f, 0.128f, 0.010f), new Vector3(-0.040f, 0.145f, 0.014f),
        };
        var handBase = new Vector3(0.06f, -0.17f, 0.42f);
        var handRot = Quaternion.Euler(-25f, 0f, -12f);
        for (int i = 0; i < 21; i++) HandTracker.ShapeJoints[i] = handBase + handRot * pose[i];
        VirtualWorld.Active = true;
        HandTracker.Visible = true;
        HandTracker.HandClicks = true;
        hand.BuildNow();
        HandTracker.Handedness = "sol";
        hand.PoseNow(false);
        // Kumanda modeli: elin solunda ikinci bir örnek
        var ctrl = new GameObject("Kumanda Onizleme").AddComponent<VirtualController>();
        ctrl.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Oyun Nesnesi.mat");
        ctrl.BuildNow();
        ctrl.PlaceNow(new Vector3(-0.13f, -0.1f, 0.38f), Vector3.up, -Vector3.forward);

        float[] yaws = { 0f, 70f };
        float[] pitches = { -6f, -8f };
        for (int i = 0; i < 8; i++)
        {
            int env = i / 2;
            for (int e = 0; e < 4; e++) envRoots[e].gameObject.SetActive(e == env);
            vw.PreviewApply((VirtualWorld.Env)env);
            camGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(pitches[i % 2], yaws[i % 2], 0f));
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"D:/unity/onizleme_{i}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
        }
        Debug.Log("GECIT ONIZLEME OK");
    }

    // İmleç testi: beyaz, gri ve siyah zeminin üstünde imleç (renk değiştiriyor mu)
    public static void PreviewCursor()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGO = new GameObject("Kamera", typeof(Camera));
        var cam = camGO.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Imlec.mat");
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Imlec Nokta.png");
        Color[] bgs = { Color.white, new Color(0.5f, 0.5f, 0.5f), Color.black, new Color(0.9f, 0.3f, 0.2f) };
        for (int i = 0; i < bgs.Length; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.transform.position = new Vector3(-1.5f + i, 0f, 3f);
            var m = new Material(Shader.Find("Unlit/Color")) { color = bgs[i] };
            q.GetComponent<Renderer>().sharedMaterial = m;
            var c = new GameObject("Imlec", typeof(SpriteRenderer));
            c.transform.position = new Vector3(-1.5f + i, 0.25f, 2.9f);
            c.transform.localScale = Vector3.one * 0.25f;
            var sr = c.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.color = i == 3 ? new Color(0.35f, 0.6f, 1f, 1f) : Color.white;
        }
        var rt = new RenderTexture(1200, 400, 24);
        cam.targetTexture = rt;
        cam.fieldOfView = 30f;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes("D:/unity/imlec_test.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Debug.Log("GECIT IMLEC TEST OK");
    }

    // Işın testi: sahnedeki "Isin" çizgisinin ayarlarıyla elden pencereye bir çizgi çiz (görünüyor mu)
    public static void PreviewRay()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var srcMat = GameObject.Find("Isin").GetComponent<MeshRenderer>().sharedMaterial;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGO = new GameObject("Kamera", typeof(Camera));
        var cam = camGO.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.45f, 0.42f, 0.38f);
        cam.fieldOfView = 32.5f;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.transform.position = new Vector3(-0.4f, 0.15f, 2.2f);
        q.transform.localScale = new Vector3(1.4f, 0.8f, 1f);
        q.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.12f, 0.12f, 0.14f) };
        var lineGO = new GameObject("Isin Test", typeof(MeshFilter), typeof(MeshRenderer));
        lineGO.GetComponent<MeshRenderer>().sharedMaterial = srcMat;
        var line = lineGO.AddComponent<RayBeam>();
        line.SetPosition(0, new Vector3(0.12f, -0.09f, 0.35f));
        line.SetPosition(1, new Vector3(-0.3f, 0.2f, 2.19f));
        line.Rebuild(Vector3.zero);
        var rt = new RenderTexture(1170, 540, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes("D:/unity/isin_test.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Debug.Log("GECIT ISIN TEST OK malzeme=" + (srcMat != null ? srcMat.shader.name + " doku=" + (srcMat.mainTexture != null) : "yok"));
    }

    // Arayüz testi: menü paneli + pencere çerçevesi + imleç halkası (neon çerçeveler nasıl görünüyor)
    public static void PreviewUI()
    {
        UiKit.Rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Yuvarlak.png");
        UiKit.HoloBase = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Holo Cerceve.mat");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Kamera", typeof(Camera)).GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.36f, 0.34f, 0.31f);
        cam.fieldOfView = 55f;

        // Pencere (koyu çerçeve + beyaz içerik + neon kenar)
        var win = UiKit.WorldCanvas("Pencere", null, new Vector2(1280f, 720f), 0.0012f);
        win.transform.position = new Vector3(0f, 0.42f, 2.2f);
        var wt = win.transform;
        UiKit.Box(wt, "Cerceve", Vector2.zero, new Vector2(1308f, 748f), new Color(0.08f, 0.09f, 0.12f, 0.88f), null, 1f);
        UiKit.Holo(wt, "Isikli Cerceve", Vector2.zero, new Vector2(1308f, 748f), 40f, new Color(0.3f, 0.82f, 1f, 0.75f), 36f, 0.7f, 70f);
        UiKit.Box(wt, "Icerik", Vector2.zero, new Vector2(1280f, 720f), new Color(0.96f, 0.96f, 0.97f, 1f), null, 1.4f);

        // Pencere başlığı
        var chip = UiKit.Box(wt, "Baslik", new Vector2(0f, 360f + 46f), new Vector2(300f, 50f), new Color(0.05f, 0.07f, 0.1f, 0.82f), null, 80f / 50f);
        UiKit.Box(chip.transform, "Nokta", new Vector2(-122f, 0f), new Vector2(12f, 12f), UiKit.HoloColor, null, 80f / 12f);
        UiKit.Label(chip.transform, "YouTube", 26, Color.white, new Vector2(8f, 0f), new Vector2(240f, 50f));

        // Menü paneli
        var dock = UiKit.WorldCanvas("Menu", null, new Vector2(720f, 190f), 0.001f);
        dock.transform.position = new Vector3(0f, -0.2f, 0.9f);
        var t = dock.transform;
        UiKit.Box(t, "Zemin", Vector2.zero, new Vector2(720f, 190f), new Color(0.05f, 0.07f, 0.1f, 0.88f), null, 1.4f);
        UiKit.Holo(t, "Isikli Cerceve", Vector2.zero, new Vector2(720f, 190f), 28f, new Color(0.3f, 0.82f, 1f, 0.6f), 24f, 1f);
        UiKit.Label(t, "21:40     %82", 20, new Color(1f, 1f, 1f, 0.85f), new Vector2(0f, 70f), new Vector2(160f, 30f));
        var colors = new[] { new Color(0.2f, 0.45f, 0.95f), new Color(0.25f, 0.28f, 0.36f), new Color(0.45f, 0.2f, 0.85f), new Color(0.18f, 0.12f, 0.4f), new Color(0.08f, 0.45f, 0.6f), new Color(0.12f, 0.55f, 0.45f), new Color(0.85f, 0.55f, 0.15f) };
        var names = new[] { "Uygulamalar", "Önüme getir", "Işın Kılıcı", "Sanal dünya", "Oda tara", "Gözlük", "Fotoğraf çek" };
        var icons = new[] { UiKit.Icon.Apps, UiKit.Icon.Recenter, UiKit.Icon.Saber, UiKit.Icon.Globe, UiKit.Icon.Scan, UiKit.Icon.Goggles, UiKit.Icon.Camera };
        for (int i = 0; i < 7; i++)
        {
            bool hover = i == 4;
            var bg = UiKit.Box(t, names[i], new Vector2(-300f + i * 100f, -5f), new Vector2(76f, 76f), hover ? Color.Lerp(colors[i], Color.white, 0.25f) : colors[i], null, 80f / 18f);
            var g = UiKit.Holo(bg.transform, "Parilti", Vector2.zero, new Vector2(76f, 76f), 9f, new Color(0.3f, 0.82f, 1f, hover ? 1f : 0.22f), 12f, 0f);
            g.transform.SetAsFirstSibling();
            UiKit.Box(bg.transform, "Simge", Vector2.zero, new Vector2(46f, 46f), Color.white, UiKit.GetIcon(icons[i]));
            UiKit.Label(bg.transform, names[i], 16, new Color(1f, 1f, 1f, 0.9f), new Vector2(0f, -54f), new Vector2(110f, 24f));
            if (hover) bg.transform.localScale = Vector3.one * 1.12f;
        }

        // Oda taraması blokları: zemin + duvar + masa
        var voxMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tarama Blogu.mat");
        voxMat.SetFloat("_Alpha", 1f);
        voxMat.SetFloat("_Floor", -1.2f);
        const float vs = 0.06f;
        System.Action<Vector3> vox = p =>
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.transform.position = p;
            c.transform.localScale = Vector3.one * vs * 0.86f;
            c.GetComponent<Renderer>().sharedMaterial = voxMat;
        };
        for (int x = 0; x < 22; x++) for (int z = 0; z < 14; z++) vox(new Vector3(0.75f + x * vs, -1.2f, 1.2f + z * vs));          // zemin
        for (int x = 0; x < 22; x++) for (int y = 0; y < 20; y++) vox(new Vector3(0.75f + x * vs, -1.2f + y * vs, 1.2f + 14 * vs)); // duvar
        for (int x = 0; x < 8; x++) for (int z = 0; z < 5; z++) vox(new Vector3(0.95f + x * vs, -0.7f, 1.35f + z * vs));          // masa

        // İmleç: nokta + kıstırma halkası
        var cur = new GameObject("Imlec", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        cur.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Imlec Nokta.png");
        cur.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Imlec.mat");
        cur.transform.position = new Vector3(0.25f, 0.5f, 2.18f);
        cur.transform.localScale = Vector3.one * 2.2f * 0.036f;
        var ring = new GameObject("Halka", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        ring.transform.SetParent(cur.transform, false);
        ring.sprite = UiKit.ThinRing;
        ring.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Imlec Halka.mat");
        ring.color = new Color(0.3f, 0.82f, 1f, 0.85f);
        ring.transform.localScale = Vector3.one * 0.8f;

        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1170, 540, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes("D:/unity/arayuz_test.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Debug.Log("GECIT ARAYUZ TEST OK holo=" + (UiKit.HoloBase != null));
    }

    // ---- Gerçek 360° fotoğraflar (D:/unity/panorama -> Assets/Resources/Panorama) ----
    const string PanoSource = "D:/unity/panorama/";
    const string PanoDir = "Assets/Resources/Panorama/";
    static readonly string[] PanoFiles = { "gece", "gunduz", "uzay", "sahil" };

    static bool[] ImportPanoramas()
    {
        Directory.CreateDirectory(PanoDir);
        var ok = new bool[PanoFiles.Length];
        for (int i = 0; i < PanoFiles.Length; i++)
        {
            string dst = PanoDir + PanoFiles[i] + ".jpg";
            string src = PanoSource + PanoFiles[i] + ".jpg";
            if (!File.Exists(dst))
            {
                if (File.Exists(src)) File.Copy(src, dst);
                else if (File.Exists(PanoSource + PanoFiles[i] + ".exr")) ConvertExr(PanoSource + PanoFiles[i] + ".exr", dst);
            }
            if (!File.Exists(dst)) continue;
            AssetDatabase.ImportAsset(dst);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = false;
            imp.isReadable = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapModeU = TextureWrapMode.Repeat;   // yatayda dikişsiz
            imp.wrapModeV = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = 8192;
            imp.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 8192,
                format = PanoFiles[i] == "gece" || PanoFiles[i] == "uzay" ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6,   // yıldızlar silinmesin
                compressionQuality = 100,
            });
            imp.SaveAndReimport();
            ok[i] = true;
            MakePanoThumb(i, dst);
        }
        return ok;
    }

    // Ortam menüsü için küçük önizleme: fotoğrafın karşıya gelen kısmı (192x192)
    static void MakePanoThumb(int i, string src)
    {
        string dst = PanoDir + "kucuk_" + PanoFiles[i] + ".jpg";
        if (!File.Exists(dst))
        {
            var big = new Texture2D(2, 2, TextureFormat.RGB24, false);
            big.LoadImage(File.ReadAllBytes(src));
            const int N = 192;
            var small = new Texture2D(N, N, TextureFormat.RGB24, false);
            float u0 = 0.5f + VirtualWorld.PanoFront[i] / 360f;
            float k = i == 0 ? 0.6f : 1f;   // gece: uygulamadaki gibi karanlık
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = Mathf.Repeat(u0 + (x / (float)N - 0.5f) * 0.22f, 1f);
                    float v = 0.55f + (y / (float)N - 0.5f) * 0.44f;
                    var c = big.GetPixelBilinear(u, v) * k;
                    c.a = 1f;
                    small.SetPixel(x, y, c);
                }
            small.Apply();
            File.WriteAllBytes(dst, small.EncodeToJPG(90));
            Object.DestroyImmediate(big);
            Object.DestroyImmediate(small);
        }
        AssetDatabase.ImportAsset(dst);
        var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
        imp.textureType = TextureImporterType.Default;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = 256;
        imp.SaveAndReimport();
    }

    // Ortam sesleri: tek kanal, sıkıştırılmış (Vorbis); uzun dalga sesi akıtılarak çalınır
    static void ImportAmbientSounds()
    {
        foreach (var name in new[] { "gece", "gunduz", "sahil" })
        {
            string path = "Assets/Resources/Ses/" + name + ".ogg";
            if (!File.Exists(path)) continue;
            AssetDatabase.ImportAsset(path);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            imp.forceToMono = true;
            imp.loadInBackground = true;
            var st = imp.defaultSampleSettings;
            st.loadType = name == "sahil" ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            st.compressionFormat = AudioCompressionFormat.Vorbis;
            st.quality = 0.45f;
            st.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            imp.defaultSampleSettings = st;
            imp.SaveAndReimport();
        }
    }

    // NASA yıldız haritası (HDR, EXR) -> telefonda gösterilebilir JPG: parlaklık ayarı + yumuşak tepe sıkıştırma
    static void ConvertExr(string src, string dst)
    {
        const string tmp = "Assets/PanoramaKaynak/kaynak.exr";
        Directory.CreateDirectory("Assets/PanoramaKaynak");
        File.Copy(src, tmp, true);
        AssetDatabase.ImportAsset(tmp);
        var imp = (TextureImporter)AssetImporter.GetAtPath(tmp);
        imp.isReadable = true;
        imp.mipmapEnabled = false;
        imp.sRGBTexture = false;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.maxTextureSize = 8192;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        var ps = new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 8192, format = TextureImporterFormat.RGBAHalf };
        imp.SetPlatformTextureSettings(ps);
        imp.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 8192, format = TextureImporterFormat.RGBAHalf });
        imp.SaveAndReimport();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tmp);
        Debug.Log("GECIT EXR " + tex.width + "x" + tex.height + " " + tex.format);
        int w = tex.width, h = tex.height;
        var data = tex.GetPixelData<ushort>(0);
        int stride = data.Length / (w * h);   // 4 (RGBA) ya da 3

        // Parlaklık dağılımı (örnekleyerek)
        var lum = new System.Collections.Generic.List<float>();
        for (int i = 0; i < w * h; i += 37)
        {
            float r = Mathf.HalfToFloat(data[i * stride]), g = Mathf.HalfToFloat(data[i * stride + 1]), b = Mathf.HalfToFloat(data[i * stride + 2]);
            lum.Add(0.2126f * r + 0.7152f * g + 0.0722f * b);
        }
        lum.Sort();
        float P(float q) => lum[Mathf.Clamp((int)(q * lum.Count), 0, lum.Count - 1)];
        Debug.Log($"GECIT EXR parlaklik p50={P(0.5f):G4} p90={P(0.9f):G4} p99={P(0.99f):G4} p999={P(0.999f):G4} max={lum[lum.Count - 1]:G4}");
        float k = Gain / Mathf.Max(P(0.99f), 1e-8f);

        var outTex = new Texture2D(w, h, TextureFormat.RGB24, false);
        var px = new Color32[w * h];
        for (int i = 0; i < w * h; i++)
        {
            float r = Mathf.HalfToFloat(data[i * stride]) * k, g = Mathf.HalfToFloat(data[i * stride + 1]) * k, b = Mathf.HalfToFloat(data[i * stride + 2]) * k;
            px[i] = new Color32(Tone(r), Tone(g), Tone(b), 255);
        }
        outTex.SetPixels32(px);
        outTex.Apply();
        File.WriteAllBytes(dst, outTex.EncodeToJPG(95));
        Object.DestroyImmediate(outTex);
        AssetDatabase.DeleteAsset(tmp);
        AssetDatabase.DeleteAsset("Assets/PanoramaKaynak");
        Debug.Log("GECIT EXR -> " + dst);
    }

    static float Gain => float.TryParse(System.Environment.GetEnvironmentVariable("GECIT_UZAY_KAZANC"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) ? g : 0.6f;

    static byte Tone(float x)
    {
        float t = 1f - Mathf.Exp(-Mathf.Max(x, 0f));           // yumuşak tepe sıkıştırma
        return (byte)Mathf.RoundToInt(Mathf.Pow(t, 1f / 2.2f) * 255f);   // doğrusal -> sRGB
    }

    // Uzay fotoğrafını yeniden üret (kazanç ayarı için): GECIT_UZAY_KAZANC=0.6
    public static void RebuildSpacePano()
    {
        AssetDatabase.DeleteAsset(PanoDir + "uzay.jpg");
        ConvertExr(PanoSource + "uzay.exr", PanoDir + "uzay.jpg");
        AssetDatabase.Refresh();
    }

    // El modeli incelemesi: içe aktarılan .glb'nin kemik adları ve dinlenme konumları (Unity uzayında)
    public static void InspectHandModel()
    {
        foreach (var side in new[] { "left", "right" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/El/" + side + ".glb");
            if (go == null) { Debug.Log("GECIT ELMODEL yok " + side); continue; }
            var inst = (GameObject)Object.Instantiate(go);
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log($"GECIT ELMODEL {side} smr={(smr != null)} kemik={(smr != null ? smr.bones.Length : 0)} kok={(smr != null && smr.rootBone != null ? smr.rootBone.name : "-")}");
            foreach (var t in inst.GetComponentsInChildren<Transform>())
                Debug.Log($"GECIT ELMODEL {side} {t.name} p={t.position.x:F4},{t.position.y:F4},{t.position.z:F4} up={t.up.x:F2},{t.up.y:F2},{t.up.z:F2} fwd={t.forward.x:F2},{t.forward.y:F2},{t.forward.z:F2}");
            Object.DestroyImmediate(inst);
        }
    }

    // Gerçek menü önizlemesi: HomeMenu'yu editörde kurar (Start yansımayla), ana panel + ortam paneli
    public static void PreviewMenu()
    {
        UiKit.Rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Yuvarlak.png");
        UiKit.HoloBase = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Holo Cerceve.mat");
        UiKit.CustomFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Inter.ttf");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Kamera", typeof(Camera)).GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.33f, 0.36f, 0.33f);
        cam.fieldOfView = 50f;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var menu = new GameObject("Menu").AddComponent<HomeMenu>();
        menu.head = cam.transform;
        typeof(HomeMenu).GetMethod("Start", flags).Invoke(menu, null);
        var dock = (Transform)typeof(HomeMenu).GetField("dockRoot", flags).GetValue(menu);
        var env = (Transform)typeof(HomeMenu).GetField("envRoot", flags).GetValue(menu);
        dock.gameObject.SetActive(true);
        dock.position = new Vector3(0f, 0.17f, 1.1f);
        env.gameObject.SetActive(true);
        env.position = new Vector3(0f, -0.2f, 1.1f);
        foreach (var g in Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) g.alpha = 1f;
        foreach (var b in Object.FindObjectsByType<UiButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            typeof(UiButton).GetMethod("Start", flags).Invoke(b, null);
            b.transform.localScale = Vector3.one;
        }
        Canvas.ForceUpdateCanvases();
        foreach (var tx in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) tx.SetAllDirty();
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(1170, 540, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes("D:/unity/menu_test.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        Debug.Log("GECIT MENU TEST OK font=" + UiKit.Font.name);
    }

    // Sinema salonu önizlemesi: salon fotoğrafı + perde penceresinin yeri (fotoğraftaki perdeyle örtüşüyor mu)
    public static void PreviewCinema()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var vw = new GameObject("Onizleme").AddComponent<VirtualWorld>();
        vw.mountainMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Daglar.mat");
        vw.cinemaSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sinema Gokyuzu.mat");
        vw.floorMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sanal Zemin.mat");
        var root = vw.BuildForPreview(VirtualWorld.Env.Sinema);
        root.position = new Vector3(0f, -1.15f, 0f);
        vw.PreviewApply(VirtualWorld.Env.Sinema);
        var cam = new GameObject("Kamera", typeof(Camera)).GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 62f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 100f;
        cam.transform.rotation = Quaternion.Euler(12f, 0f, 0f);   // biraz aşağı: koltuk da görünsün
        var screen = new GameObject("Perde").AddComponent<CurvedScreen>();
        screen.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Kavisli Perde.mat");
        screen.ShowTexture(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Panorama/kucuk_gunduz.jpg"), Vector3.zero, Vector3.forward);
        var rt = new RenderTexture(1170, 540, 24);
        cam.targetTexture = rt;
        float[] pitches = { -3f, 50f };
        for (int n = 0; n < 2; n++)
        {
            cam.transform.rotation = Quaternion.Euler(pitches[n], n == 1 ? 25f : 0f, 0f);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"D:/unity/sinema_test{n}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
        }
        Debug.Log("GECIT SINEMA TEST OK");
    }

    public static void Build()
    {
        SetEmbeddedAndroidTools();
        // Bilgisayarda bellek az (C: dolu, sayfa dosyası büyüyemiyor): il2cpp aynı anda 3 çekirdek kullansın
        PlayerSettings.SetAdditionalIl2CppArgs("--jobs=2");
        // Daha az genel (generic) kod üret: dönüştürme daha az bellek ister
        PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Android, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
        Directory.CreateDirectory("Builds");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("GECIT BUILD RESULT: " + report.summary.result + " size=" + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
