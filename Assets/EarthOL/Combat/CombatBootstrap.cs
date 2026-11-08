using System.Collections;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>
    /// 血条系统的接入层：在运行时找到 ARCoreMainSceneBootstrap 建好的系统，
    /// 组装血量、血条 UI、攻击历史、击败流程与结算，并接入选择/投掷/按压的成功事件。
    ///
    /// 本类是唯一了解 ARCore 各系统内部结构的模块；血量系统核心逻辑不依赖具体实现。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CombatBootstrap : MonoBehaviour
    {
        const string ConfigResourcePath = "Combat/CombatConfig";

        [SerializeField, Min(0.02f)] float healthBarAnchorHeight = 0.16f;

        ARCoreMainSceneBootstrap bootstrap;
        AttackDispatcher dispatcher;
        HealthSystem healthSystem;
        AttackHistory history;
        DefeatController defeatController;
        ResultUI resultUI;
        HealthSettingsOverlay settingsOverlay;
        HealthBarUI healthBar;
        HealthSettings settings;
        GameObject healthBarAnchor;
        AttackDefinition[] configuredAttacks;

        /// <summary>预留的物体视觉接口（同伴在此订阅并播放效果）。</summary>
        public DefeatController DefeatController => defeatController;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (FindObjectOfType<CombatBootstrap>() != null) return;
            var go = new GameObject("Combat System");
            go.AddComponent<CombatBootstrap>();
        }

        void Start()
        {
            StartCoroutine(BindWhenReady());
        }

        IEnumerator BindWhenReady()
        {
            while (bootstrap == null)
            {
                bootstrap = FindObjectOfType<ARCoreMainSceneBootstrap>();
                yield return null;
            }

            while (bootstrap.BodyRig == null || bootstrap.PoseTracker == null ||
                   bootstrap.Spawner == null || bootstrap.PersonDeformer == null)
            {
                yield return null;
            }

            BuildSubsystems();
            Wire();
            Debug.Log("[Combat] 血条系统已接入：模式默认" +
                      (settings != null && settings.HealthBarModeEnabled ? "开启" : "关闭") +
                      "，档位=" + (settings != null ? settings.Tier.ToString() : "Medium"));
        }

        void BuildSubsystems()
        {
            dispatcher = gameObject.AddComponent<AttackDispatcher>();
            history = gameObject.AddComponent<AttackHistory>();
            healthSystem = gameObject.AddComponent<HealthSystem>();
            defeatController = gameObject.AddComponent<DefeatController>();
            resultUI = gameObject.AddComponent<ResultUI>();
            settingsOverlay = gameObject.AddComponent<HealthSettingsOverlay>();

            LoadConfiguration();
            dispatcher.Register(GetAttacks());
            settingsOverlay.Configure(settings);

            healthBarAnchor = new GameObject("HealthBarAnchor");
            healthBar = healthBarAnchor.AddComponent<HealthBarUI>();
            healthBar.Bind(healthBarAnchor.transform, bootstrap.ARCamera);
            healthBar.Hide();

            defeatController.Configure(healthSystem);
        }

        void LoadConfiguration()
        {
            var config = Resources.Load<CombatConfig>(ConfigResourcePath);
            if (config != null)
            {
                settings = config.healthSettings != null
                    ? config.healthSettings
                    : CombatDefaults.CreateDefaultHealthSettings();
                configuredAttacks = config.attacks;
            }
            else
            {
                settings = CombatDefaults.CreateDefaultHealthSettings();
                configuredAttacks = null;
            }
        }

        AttackDefinition[] GetAttacks()
        {
            if (configuredAttacks != null && configuredAttacks.Length > 0)
                return configuredAttacks;

            var catalog = bootstrap.Spawner != null ? bootstrap.Spawner.Catalog : null;
            return CombatDefaults.BuildDefaultAttacks(catalog);
        }

        void Wire()
        {
            bootstrap.PoseTracker.PersonSelected += OnPersonSelected;

            settingsOverlay.ModeChanged += OnModeChanged;

            healthSystem.Initialized += OnHealthInitialized;
            healthSystem.Damaged += OnHealthDamaged;

            defeatController.SettlementRequested += OnSettlementRequested;
            resultUI.ReselectRequested += OnReselectRequested;
        }

        void OnPersonSelected()
        {
            // 攻击历史独立于血条模式：每次选择都开一轮新记录。
            history.Begin();
            defeatController.ResetForNewRound();

            if (settings != null && settings.HealthBarModeEnabled)
                StartHealthRound();
            else
                healthBar.Hide();
        }

        void StartHealthRound()
        {
            var maxHealth = settings != null ? settings.CurrentMaxHealth : 100f;
            healthSystem.Initialize(maxHealth);
        }

        void OnHealthInitialized(float maxHealth)
        {
            healthBar.Bind(healthBarAnchor.transform, bootstrap.ARCamera);
            healthBar.Initialize(maxHealth);
            healthBar.Show();
        }

        void OnHealthDamaged(float damage, float currentHealth, float maxHealth)
        {
            healthBar.OnDamaged(damage, currentHealth, maxHealth);
        }

        void OnModeChanged(bool enabled)
        {
            if (enabled)
            {
                // 已选中物体时开启：立即为当前选中物体初始化血量（不重置本轮历史）。
                if (bootstrap != null && bootstrap.PoseTracker != null && bootstrap.PoseTracker.HasPose)
                    StartHealthRound();
            }
            else
            {
                healthSystem.Deactivate();
                healthBar.Hide();
                defeatController.ResetForNewRound();
            }
        }

        void OnSettlementRequested()
        {
            resultUI.Show(history.GetMerged());
        }

        void OnReselectRequested()
        {
            history.Clear();
            healthSystem.Deactivate();
            healthBar.Hide();
            defeatController.ResetForNewRound();

            if (bootstrap != null && bootstrap.Spawner != null)
                bootstrap.Spawner.ClearProjectiles();

            if (bootstrap != null && bootstrap.PoseTracker != null)
                bootstrap.PoseTracker.RequestReselect();
        }

        void LateUpdate()
        {
            if (healthBarAnchor == null || bootstrap == null || bootstrap.BodyRig == null) return;

            if (TryGetAnchorPosition(bootstrap.BodyRig, out var position))
                healthBarAnchor.transform.position = position;
        }

        bool TryGetAnchorPosition(BodyProxyRig rig, out Vector3 position)
        {
            position = Vector3.zero;
            var any = false;
            var top = float.MinValue;
            var center = Vector3.zero;

            foreach (var part in rig.Parts)
            {
                if (part == null || !part.gameObject.activeInHierarchy) continue;
                var collider = part.GetComponent<Collider>();
                if (collider == null || !collider.enabled) continue;

                var bounds = collider.bounds;
                if (bounds.max.y > top)
                {
                    top = bounds.max.y;
                    center = bounds.center;
                    any = true;
                }
            }

            if (!any) return false;
            position = new Vector3(center.x, top + healthBarAnchorHeight, center.z);
            return true;
        }

        void OnDestroy()
        {
            if (bootstrap != null && bootstrap.PoseTracker != null)
                bootstrap.PoseTracker.PersonSelected -= OnPersonSelected;
        }
    }
}
