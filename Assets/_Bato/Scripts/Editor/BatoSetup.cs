using System.Collections.Generic;
using System.IO;
using Bato.Water;
using Features.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bato.EditorTools
{
    /// <summary>
    /// Génère toute la scène de jeu et les prefabs réseau en un clic.
    /// Rejouable : relancer écrase proprement ce qui existe.
    ///
    /// Menu : Bato > Générer l'arène
    /// </summary>
    public static class BatoSetup
    {
        const string k_Root = "Assets/_Bato";
        const string k_PrefabDir = k_Root + "/Prefabs";
        const string k_MaterialDir = k_Root + "/Materials";
        const string k_MeshDir = k_Root + "/Meshes";
        const string k_SceneDir = k_Root + "/Scenes";
        const string k_WaveSettingsPath = k_Root + "/WaveSettings.asset";
        const string k_OceanMeshPath = k_MeshDir + "/OceanGrid.asset";
        const string k_OceanMaterialPath = k_MaterialDir + "/Ocean.mat";
        const string k_ScenePath = k_SceneDir + "/Arena.unity";
        const string k_BoatPath = k_PrefabDir + "/Boat.prefab";
        const string k_BallPath = k_PrefabDir + "/Cannonball.prefab";
        const string k_PrefabListPath = k_Root + "/BatoNetworkPrefabs.asset";
        const string k_InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        const int k_SpawnCount = 6;
        const float k_ArenaRadius = 45f;

        // La grille de mer déborde largement l'arène pour que l'horizon reste de l'eau.
        const float k_OceanSize = 190f;
        const int k_OceanResolution = 140;   // quads par côté -> 141² sommets, sous la limite 16 bits

        /// <summary>
        /// Régénère uniquement les prefabs, sans toucher à la scène. À utiliser dès que
        /// quelqu'un a commencé à éditer Arena.unity à la main : le chemin des prefabs ne change
        /// pas, donc leur GUID non plus, et les références de la scène survivent.
        /// </summary>
        [MenuItem("Bato/Régénérer les prefabs seulement", priority = 1)]
        public static void GeneratePrefabsOnly()
        {
            EnsureFolders();

            var ballPrefab = CreateCannonballPrefab();
            var boatPrefab = CreateBoatPrefab(ballPrefab);
            CreatePrefabList(ballPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Bato] Prefabs régénérés ({boatPrefab.name}, {ballPrefab.name}). Scène intacte.");
        }

        [MenuItem("Bato/Générer l'arène (écrase la scène)", priority = 0)]
        public static void GenerateAll()
        {
            EnsureFolders();

            var ballPrefab = CreateCannonballPrefab();
            var boatPrefab = CreateBoatPrefab(ballPrefab);
            var prefabList = CreatePrefabList(ballPrefab);

            BuildScene(boatPrefab, prefabList);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Bato] Arène générée. Ouvre Assets/_Bato/Scenes/Arena.unity et lance Play.");
        }

        // ------------------------------------------------------------ Dossiers

        static void EnsureFolders()
        {
            foreach (var dir in new[] { k_Root, k_PrefabDir, k_MaterialDir, k_MeshDir, k_SceneDir })
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------ Prefabs

        static GameObject CreateCannonballPrefab()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "Cannonball";
            root.transform.localScale = Vector3.one * 0.35f;

            var collider = root.GetComponent<SphereCollider>();
            collider.isTrigger = true;

            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial("Cannonball", new Color(0.12f, 0.12f, 0.14f));

            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;              // tir tendu : bien plus facile à viser en jam
            body.linearDamping = 0f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            root.AddComponent<NetworkObject>();

            var transformSync = root.AddComponent<NetworkTransform>();
            transformSync.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            transformSync.SyncScaleX = transformSync.SyncScaleY = transformSync.SyncScaleZ = false;

            root.AddComponent<Cannonball>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_BallPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject CreateBoatPrefab(GameObject ballPrefab)
        {
            var root = new GameObject("Boat");

            // --- coque (visuel + hitbox)
            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(root.transform, false);
            hull.transform.localScale = new Vector3(1.6f, 0.7f, 4f);
            hull.GetComponent<MeshRenderer>().sharedMaterial =
                CreateMaterial("Hull", new Color(0.55f, 0.32f, 0.18f));
            Object.DestroyImmediate(hull.GetComponent<BoxCollider>());

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mast.name = "Sail";
            mast.transform.SetParent(root.transform, false);
            mast.transform.localPosition = new Vector3(0f, 1.4f, -0.2f);
            mast.transform.localScale = new Vector3(0.15f, 2.2f, 1.6f);
            mast.GetComponent<MeshRenderer>().sharedMaterial =
                CreateMaterial("Sail", new Color(0.92f, 0.9f, 0.85f));
            Object.DestroyImmediate(mast.GetComponent<BoxCollider>());

            // Une proue pour voir dans quel sens on va.
            var bow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bow.name = "Bow";
            bow.transform.SetParent(root.transform, false);
            bow.transform.localPosition = new Vector3(0f, 0f, 2.2f);
            bow.transform.localScale = new Vector3(0.8f, 0.6f, 1.2f);
            bow.GetComponent<MeshRenderer>().sharedMaterial =
                CreateMaterial("Bow", new Color(0.75f, 0.2f, 0.2f));
            Object.DestroyImmediate(bow.GetComponent<BoxCollider>());

            // --- canons : bordée bâbord + tribord
            var muzzleLeft = new GameObject("MuzzleLeft");
            muzzleLeft.transform.SetParent(root.transform, false);
            muzzleLeft.transform.localPosition = new Vector3(-1f, 0.55f, 0.2f);
            muzzleLeft.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);

            var muzzleRight = new GameObject("MuzzleRight");
            muzzleRight.transform.SetParent(root.transform, false);
            muzzleRight.transform.localPosition = new Vector3(1f, 0.55f, 0.2f);
            muzzleRight.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // --- physique
            var hitbox = root.AddComponent<BoxCollider>();
            hitbox.size = new Vector3(1.6f, 1.2f, 4.4f);
            hitbox.center = new Vector3(0f, 0.25f, 0f);

            // Gravité, damping et contraintes sont posés par BoatMovementController dans son
            // Awake : on ne règle ici que ce qu'il ne touche pas (masse pour les abordages,
            // détection de collision pour ne pas traverser les murs à pleine vitesse).
            var body = root.AddComponent<Rigidbody>();
            body.mass = 60f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // --- réseau
            root.AddComponent<NetworkObject>();

            var transformSync = root.AddComponent<NetworkTransform>();
            transformSync.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // client-authoritative
            transformSync.SyncScaleX = transformSync.SyncScaleY = transformSync.SyncScaleZ = false;
            transformSync.Interpolate = true;

            // --- pilotage : les scripts de Features.Player, tels quels
            var playerInput = root.AddComponent<PlayerInput>();
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(k_InputActionsPath);
            if (inputActions == null)
                Debug.LogWarning($"[Bato] {k_InputActionsPath} introuvable — assigne les actions à la main sur le prefab Boat.");
            SetField(playerInput, "m_Actions", inputActions);
            SetStringField(playerInput, "m_DefaultActionMap", "Player");
            SetIntField(playerInput, "m_NotificationBehavior", 3); // Invoke C# Events : on poll, pas de SendMessage

            var inputSource = root.AddComponent<PlayerInputSource>();
            var movement = root.AddComponent<BoatMovementController>();
            SetField(movement, "_inputSource", inputSource);

            // --- flottaison : désactivée dans le prefab, allumée au spawn pour le propriétaire
            var buoyancy = root.AddComponent<BoatBuoyancy>();
            SetProbeOffsets(buoyancy, hitbox);
            buoyancy.enabled = false;

            // --- couche réseau et combat
            root.AddComponent<BoatNetworkAuthority>();
            var health = root.AddComponent<BoatHealth>();
            var cannon = root.AddComponent<BoatCannon>();

            SetField(cannon, "m_CannonballPrefab", ballPrefab);
            SetArrayField(cannon, "m_Muzzles", new Object[] { muzzleLeft.transform, muzzleRight.transform });

            SetArrayField(health, "m_VisualsToHideOnDeath", new Object[] { hull, mast, bow });
            SetArrayField(health, "m_CollidersToDisableOnDeath", new Object[] { hitbox });

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_BoatPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Place les quatre sondes aux coins de la coque et accorde la profondeur de sonde à la
        /// force de poussée, pour que le bateau se stabilise pile à sa ligne de flottaison.
        ///
        /// À l'équilibre, poussée = poids, donc submersion = 1 / force. Le bateau s'enfonce donc
        /// de submersion × profondeurDeSonde. En posant profondeurDeSonde = -yDeSonde × force, la
        /// coque se stabilise avec son origine au niveau de l'eau, quelle que soit sa taille.
        /// </summary>
        static void SetProbeOffsets(BoatBuoyancy buoyancy, BoxCollider hull)
        {
            const float buoyancyStrength = 1.6f;

            var extents = hull.size * 0.5f;
            float x = extents.x * 0.85f;
            float z = extents.z * 0.8f;
            float y = -extents.y * 0.5f;   // sondes sous la ligne de flottaison

            var serialized = new SerializedObject(buoyancy);
            serialized.FindProperty("m_BuoyancyStrength").floatValue = buoyancyStrength;
            serialized.FindProperty("m_ProbeDepth").floatValue = -y * buoyancyStrength;

            var property = serialized.FindProperty("m_ProbeOffsets");
            property.ClearArray();

            var offsets = new[]
            {
                new Vector3(-x, y,  z),
                new Vector3( x, y,  z),
                new Vector3(-x, y, -z),
                new Vector3( x, y, -z),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                property.InsertArrayElementAtIndex(i);
                property.GetArrayElementAtIndex(i).vector3Value = offsets[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static NetworkPrefabsList CreatePrefabList(GameObject ballPrefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, k_PrefabListPath);
            }

            var serialized = new SerializedObject(list);
            var listProperty = serialized.FindProperty("List");
            listProperty.ClearArray();
            listProperty.InsertArrayElementAtIndex(0);
            listProperty.GetArrayElementAtIndex(0).FindPropertyRelative("Prefab").objectReferenceValue = ballPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(list);
            return list;
        }

        // -------------------------------------------------------------- Scène

        static void BuildScene(GameObject boatPrefab, NetworkPrefabsList prefabList)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateSea();
            var spawnPoints = CreateSpawnPoints();

            CreateNetworkManager(boatPrefab, prefabList);
            new GameObject("SessionRunner").AddComponent<SessionRunner>();

            var arena = new GameObject("ArenaBootstrap");
            arena.AddComponent<NetworkObject>();
            var bootstrap = arena.AddComponent<ArenaBootstrap>();
            SetArrayField(bootstrap, "m_SpawnPoints", spawnPoints);

            CreateCamera();
            CreateUI();

            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AddSceneToBuildSettings(k_ScenePath);
        }

        static void CreateLighting()
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            light.shadows = LightShadows.Soft;
        }

        static void CreateSea()
        {
            // La mer n'a pas de collider : la flottaison est une force, pas une collision. Un sol
            // solide se battrait avec les sondes de BoatBuoyancy.
            var ocean = new GameObject("Ocean");
            ocean.AddComponent<MeshFilter>().sharedMesh = CreateOceanMesh();

            var renderer = ocean.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateOceanMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var waveField = new GameObject("WaveField");
            waveField.AddComponent<NetworkObject>();
            var field = waveField.AddComponent<WaveField>();
            SetField(field, "m_Settings", CreateWaveSettings());

            // Murs invisibles : on ne sort pas de l'arène.
            var walls = new GameObject("Walls");
            for (int i = 0; i < 4; i++)
            {
                var wall = new GameObject($"Wall{i}");
                wall.transform.SetParent(walls.transform, false);
                float angle = i * 90f;
                wall.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                wall.transform.position = wall.transform.rotation * (Vector3.forward * k_ArenaRadius);
                var box = wall.AddComponent<BoxCollider>();
                box.size = new Vector3(k_ArenaRadius * 2.2f, 12f, 1f);
            }
        }

        // ------------------------------------------------------------- Océan

        static WaveSettings CreateWaveSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WaveSettings>(k_WaveSettingsPath);
            if (settings != null) return settings;   // ne jamais écraser un réglage déjà tuné

            settings = ScriptableObject.CreateInstance<WaveSettings>();
            AssetDatabase.CreateAsset(settings, k_WaveSettingsPath);
            return settings;
        }

        static Material CreateOceanMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_OceanMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Bato/Ocean");
            if (shader == null)
            {
                Debug.LogError("[Bato] Shader 'Bato/Ocean' introuvable — la mer sera rose. Vérifie Assets/_Bato/Shaders/Ocean.shader.");
                return CreateMaterial("OceanFallback", new Color(0.12f, 0.35f, 0.55f));
            }

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, k_OceanMaterialPath);
            return material;
        }

        /// <summary>
        /// Grille plate régulière. Toute la forme vient du shader : le mesh n'est qu'un support
        /// de sommets, donc pas de normales ni d'UV à générer.
        /// </summary>
        static Mesh CreateOceanMesh()
        {
            int side = k_OceanResolution + 1;
            int vertexCount = side * side;

            var vertices = new Vector3[vertexCount];
            var indices = new int[k_OceanResolution * k_OceanResolution * 6];

            float step = k_OceanSize / k_OceanResolution;
            float origin = -k_OceanSize * 0.5f;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    vertices[z * side + x] = new Vector3(origin + x * step, 0f, origin + z * step);
                }
            }

            int index = 0;
            for (int z = 0; z < k_OceanResolution; z++)
            {
                for (int x = 0; x < k_OceanResolution; x++)
                {
                    int bottomLeft = z * side + x;
                    int topLeft = bottomLeft + side;

                    indices[index++] = bottomLeft;
                    indices[index++] = topLeft;
                    indices[index++] = topLeft + 1;

                    indices[index++] = bottomLeft;
                    indices[index++] = topLeft + 1;
                    indices[index++] = bottomLeft + 1;
                }
            }

            var mesh = new Mesh { name = "OceanGrid" };
            mesh.indexFormat = vertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = indices;

            // Les sommets sont déplacés dans le vertex shader : Unity ne le sait pas et culerait
            // la mer dès qu'on regarde vers l'horizon. On gonfle les bornes à la main.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(k_OceanSize, 20f, k_OceanSize));

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(k_OceanMeshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, k_OceanMeshPath);
            return mesh;
        }

        static Object[] CreateSpawnPoints()
        {
            var parent = new GameObject("SpawnPoints");
            var points = new List<Object>();

            for (int i = 0; i < k_SpawnCount; i++)
            {
                float angle = i / (float)k_SpawnCount * Mathf.PI * 2f;
                var position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (k_ArenaRadius * 0.6f);

                var point = new GameObject($"Spawn{i}");
                point.transform.SetParent(parent.transform, false);
                point.transform.position = position;
                point.transform.rotation = Quaternion.LookRotation(-position.normalized, Vector3.up);
                points.Add(point.transform);
            }

            return points.ToArray();
        }

        static NetworkManager CreateNetworkManager(GameObject boatPrefab, NetworkPrefabsList prefabList)
        {
            var go = new GameObject("NetworkManager");
            var manager = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();

            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.PlayerPrefab = boatPrefab;
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.EnableSceneManagement = false; // une seule scène : rien à gérer

            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);

            EditorUtility.SetDirty(manager);
            return manager;
        }

        static void CreateCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.35f, 0.55f, 0.7f);
            go.AddComponent<AudioListener>();
            go.AddComponent<FollowLocalBoat>();
            go.transform.position = new Vector3(0f, 14f, -18f);
            go.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
        }

        // ----------------------------------------------------------------- UI

        static void CreateUI()
        {
            // EventSystem compatible Input System (le module legacy plante si l'ancien input est off).
            var eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            var uiModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(k_InputActionsPath);
            if (inputActions != null) uiModule.actionsAsset = inputActions;

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Les deux contrôleurs vivent sur le Canvas, pas sur les panneaux qu'ils masquent :
            // un composant posé sur un objet désactivé ne reçoit plus Update et ne pourrait
            // jamais réafficher son propre panneau.
            BuildConnectPanel(canvasGo);
            BuildHud(canvasGo);
        }

        static void BuildConnectPanel(GameObject canvas)
        {
            var parent = canvas.transform;
            var panel = NewUIObject("ConnectPanel", parent);
            Stretch(panel.GetComponent<RectTransform>());
            var background = panel.AddComponent<Image>();
            background.color = new Color(0.05f, 0.09f, 0.14f, 0.92f);

            MakeText(panel.transform, "Title", "BATO", 96, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.82f), new Vector2(900f, 140f));

            var hostButton = MakeButton(panel.transform, "HostButton", "HÉBERGER UNE PARTIE",
                new Vector2(0.5f, 0.62f), new Vector2(520f, 80f));

            var codeField = MakeInputField(panel.transform, "CodeField", "code de partie",
                new Vector2(0.5f, 0.50f), new Vector2(520f, 70f));

            var joinButton = MakeButton(panel.transform, "JoinButton", "REJOINDRE",
                new Vector2(0.5f, 0.40f), new Vector2(520f, 80f));

            var directHost = MakeButton(panel.transform, "DirectHostButton", "Héberger en local (sans UGS)",
                new Vector2(0.5f, 0.29f), new Vector2(430f, 52f));

            var directJoin = MakeButton(panel.transform, "DirectJoinButton", "Rejoindre par IP (champ ci-dessus)",
                new Vector2(0.5f, 0.22f), new Vector2(430f, 52f));

            var status = MakeText(panel.transform, "StatusLabel", "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.11f), new Vector2(1200f, 90f));

            var connectionUI = canvas.AddComponent<ConnectionUI>();
            SetField(connectionUI, "m_Panel", panel);
            SetField(connectionUI, "m_HostButton", hostButton);
            SetField(connectionUI, "m_JoinButton", joinButton);
            SetField(connectionUI, "m_CodeField", codeField);
            SetField(connectionUI, "m_DirectHostButton", directHost);
            SetField(connectionUI, "m_DirectJoinButton", directJoin);
            SetField(connectionUI, "m_StatusLabel", status);
        }

        static void BuildHud(GameObject canvas)
        {
            var parent = canvas.transform;
            var root = NewUIObject("HUD", parent);
            Stretch(root.GetComponent<RectTransform>());

            var joinCode = MakeText(root.transform, "JoinCodeLabel", "", 34, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(600f, 60f), new Vector2(320f, -50f));

            var scoreboard = MakeText(root.transform, "ScoreboardLabel", "", 30, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(460f, 360f), new Vector2(-250f, -200f));

            var healthLabel = MakeText(root.transform, "HealthLabel", "", 40, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(400f, 60f), new Vector2(230f, 130f));

            // Barre de vie : fond + remplissage.
            var barBack = NewUIObject("HealthBarBack", root.transform);
            var barBackRect = barBack.GetComponent<RectTransform>();
            barBackRect.anchorMin = barBackRect.anchorMax = new Vector2(0f, 0f);
            barBackRect.sizeDelta = new Vector2(400f, 26f);
            barBackRect.anchoredPosition = new Vector2(230f, 80f);
            barBack.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var barFill = NewUIObject("HealthBarFill", barBack.transform);
            Stretch(barFill.GetComponent<RectTransform>());
            var fillImage = barFill.AddComponent<Image>();
            fillImage.color = new Color(0.85f, 0.25f, 0.2f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 1f;

            var hud = canvas.AddComponent<HUD>();
            SetField(hud, "m_Root", root);
            SetField(hud, "m_HealthLabel", healthLabel);
            SetField(hud, "m_HealthFill", fillImage);
            SetField(hud, "m_ScoreboardLabel", scoreboard);
            SetField(hud, "m_JoinCodeLabel", joinCode);

            root.SetActive(false);
        }

        // ------------------------------------------------------- Helpers UI

        static Font s_Font;
        static Font UIFont => s_Font != null
            ? s_Font
            : s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static GameObject NewUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 size, Vector2? offset = null)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset ?? Vector2.zero;
        }

        static Text MakeText(Transform parent, string name, string content, int fontSize,
            TextAnchor alignment, Vector2 anchor, Vector2 size, Vector2? offset = null)
        {
            var go = NewUIObject(name, parent);
            Place(go.GetComponent<RectTransform>(), anchor, size, offset);

            var text = go.AddComponent<Text>();
            text.font = UIFont;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 anchor, Vector2 size)
        {
            var go = NewUIObject(name, parent);
            Place(go.GetComponent<RectTransform>(), anchor, size);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.16f, 0.42f, 0.62f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var text = MakeText(go.transform, "Label", label, Mathf.RoundToInt(size.y * 0.4f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size);
            Stretch(text.rectTransform);

            return button;
        }

        static InputField MakeInputField(Transform parent, string name, string placeholder,
            Vector2 anchor, Vector2 size)
        {
            var go = NewUIObject(name, parent);
            Place(go.GetComponent<RectTransform>(), anchor, size);

            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);

            var text = MakeText(go.transform, "Text", "", 34, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), size);
            Stretch(text.rectTransform);
            text.supportRichText = false;

            var placeholderText = MakeText(go.transform, "Placeholder", placeholder, 30,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), size);
            Stretch(placeholderText.rectTransform);
            placeholderText.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderText.fontStyle = FontStyle.Italic;

            var field = go.AddComponent<InputField>();
            field.targetGraphic = image;
            field.textComponent = text;
            field.placeholder = placeholderText;
            field.characterLimit = 24;

            return field;
        }

        // ---------------------------------------------------------- Helpers

        static Material CreateMaterial(string name, Color color)
        {
            string path = $"{k_MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Assigne un champ [SerializeField] privé sans avoir à le rendre public.</summary>
        static void SetField(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[Bato] Champ '{fieldName}' introuvable sur {target.GetType().Name}.");
                return;
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetStringField(Object target, string fieldName, string value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[Bato] Champ '{fieldName}' introuvable sur {target.GetType().Name}.");
                return;
            }
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetIntField(Object target, string fieldName, int value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[Bato] Champ '{fieldName}' introuvable sur {target.GetType().Name}.");
                return;
            }
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArrayField(Object target, string fieldName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogError($"[Bato] Champ '{fieldName}' introuvable sur {target.GetType().Name}.");
                return;
            }

            property.ClearArray();
            for (int i = 0; i < values.Length; i++)
            {
                property.InsertArrayElementAtIndex(i);
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;

            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
