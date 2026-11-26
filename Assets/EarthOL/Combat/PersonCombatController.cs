using System;
using System.Collections.Generic;
using EarthOL.ARCore;
using EarthOL.BodyThrow;
using UnityEngine;

namespace EarthOL.Combat
{
    /// <summary>Adapts wang combat rules to the existing shared person interactions.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class PersonCombatController : MonoBehaviour
    {
        Camera m_Camera;
        PersonTrackingManager m_Tracking;
        PersonDeformationSurface m_Surface;
        Person3DThrowController m_Thrower;
        BodyProjectileCatalog m_Catalog;
        ComboConfig m_Combo;
        AttackDamageConfig m_Damage;
        CombatConfig m_Config;
        readonly List<ScriptableObject> m_Owned = new List<ScriptableObject>();
        readonly Dictionary<string, AttackDefinition> m_Attacks = new Dictionary<string, AttackDefinition>();
        readonly Dictionary<int, CombatRound> m_Rounds = new Dictionary<int, CombatRound>();
        readonly List<int> m_Expired = new List<int>();
        readonly HashSet<int> m_GestureHits = new HashSet<int>();
        readonly Dictionary<int, float> m_HoldHits = new Dictionary<int, float>();
        readonly Queue<CombatRound> m_Results = new Queue<CombatRound>();
        CombatRound m_Result;
        GameMode m_Feature;
        bool m_Active, m_Pressed, m_Swiping, m_Holding, m_Settings;
        int m_Pointer, m_PressPerson = -1, m_NextToken, m_Session, m_HealthTier = 1;
        Vector2 m_Origin, m_Previous, m_HoldPoint;
        float m_PressTime, m_FeedbackUntil;
        string m_Feedback;
        Vector2 m_FeedbackPoint;
        Color m_FeedbackColor;
        int m_UiFrame = -1;
        const float SwipeThreshold = 40f;
        const float HoldInterval = .35f;

        public bool Active => m_Active;
        public bool BlocksInput => m_Active && (m_Settings || m_Result != null);
        public bool Holding => m_Holding;
        public int Session => m_Session;
        public IReadOnlyDictionary<int, CombatRound> Rounds => m_Rounds;
        public event Action<AttackOutcome> AttackResolved;
        public event Action<CombatRound> Defeated;
        public event Action<int> RoundRestarted;

        public void Configure(Camera camera, PersonTrackingManager tracking,
            PersonDeformationSurface surface, Person3DThrowController thrower)
        {
            m_Camera = camera; m_Tracking = tracking; m_Surface = surface; m_Thrower = thrower;
            m_Catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
            m_Config = Resources.Load<CombatConfig>("Combat/CombatConfig");
            m_Combo = m_Config != null && m_Config.comboConfig != null ? m_Config.comboConfig : Own<ComboConfig>();
            m_Damage = m_Config != null && m_Config.attackDamageConfig != null ? m_Config.attackDamageConfig : Own<AttackDamageConfig>();
            Register("press", "按压", "", 10f);
            Register("swipe", "挥扫", "", 8f);
            Register("StickyHandHold", "拍打", "粘粘手", 12f);
            if (m_Catalog != null && m_Catalog.entries != null)
                foreach (var entry in m_Catalog.entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.key))
                    {
                        float damage = entry.key == "Brain" ? 25f : entry.key == "Arrow" ? 20f : entry.key == "StickyHand" ? 15f : 10f;
                        Register(entry.key, "投掷", entry.displayName, damage);
                    }
            if (m_Config != null && m_Config.attacks != null)
                foreach (var attack in m_Config.attacks)
                    if (attack != null && !string.IsNullOrEmpty(attack.AttackId)) m_Attacks[attack.AttackId] = attack;
            m_Thrower.InteractionConfirmed += OnInteraction;
            m_Thrower.InteractionContext = CaptureRound;
        }

        T Own<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); m_Owned.Add(value); return value; }

        void Register(string id, string verb, string item, float damage)
        {
            var attack = Own<AttackDefinition>();
            attack.Configure(id, verb, item, "次", damage, AttackDefinition.InferAttackType(id));
            m_Attacks[id] = attack;
        }

        public void BeginSession(bool active, int session)
        {
            ResetRounds();
            m_Session = session;
            m_Active = active;
            ApplyInputPolicy();
        }

        public void ResetRounds()
        {
            CancelGesture();
            m_Holding = false;
            m_Rounds.Clear(); m_Result = null; m_Results.Clear(); m_Settings = false;
            m_Feedback = null; m_FeedbackUntil = 0;
            if (m_Thrower != null) m_Thrower.CancelGesture();
            ApplyInputPolicy();
        }

        public void PauseTracking()
        {
            CancelGesture();
            m_Holding = false;
            m_Surface?.EndPointer();
            m_Thrower?.CancelGesture();
            foreach (var round in m_Rounds.Values) round.BreakCombo();
            ApplyInputPolicy();
        }

        public void OnFeatureChanged(GameMode feature)
        {
            CancelGesture();
            m_Holding = false;
            m_Surface?.EndPointer();
            foreach (var round in m_Rounds.Values) round.BreakCombo();
            m_Feature = feature;
            ApplyInputPolicy();
        }

        void ApplyInputPolicy()
        {
            if (m_Surface != null) m_Surface.ReadPointerInput = !m_Active || m_Feature == GameMode.Deform;
            if (m_Thrower != null) m_Thrower.ReadPointerInput = false;
        }

        float NewRoundHealth()
        {
            if (m_Config != null && m_Config.healthTiers != null && m_Config.healthTiers.Length > m_HealthTier)
                return Mathf.Max(1f, m_Config.healthTiers[m_HealthTier]);
            return m_HealthTier == 0 ? 60f : m_HealthTier == 2 ? 240f : 120f;
        }

        public CombatRound FindRound(int personId) => m_Rounds.TryGetValue(personId, out var round) ? round : null;

        public int CaptureRound(int personId)
        {
            if (!m_Active || m_Tracking == null) return 0;
            var person = m_Tracking.Find(personId);
            if (person == null || !person.IsValid || person.trackingConfidence < .2f) return 0;
            if (!m_Rounds.TryGetValue(personId, out var round))
            {
                round = new CombatRound(personId, ++m_NextToken, NewRoundHealth());
                m_Rounds.Add(personId, round);
            }
            return round.Defeated ? 0 : round.Token;
        }

        public bool Confirm(PersonInteractionHit hit) => Confirm(hit, false);

        bool Confirm(PersonInteractionHit hit, bool inFlight)
        {
            if (!m_Active || BlocksInput && !inFlight || !m_Attacks.TryGetValue(hit.actionId ?? "", out var attack)) return false;
            var person = m_Tracking.Find(hit.personId);
            if (person == null || !person.IsValid || person.trackingConfidence < .2f ||
                !m_Rounds.TryGetValue(hit.personId, out var round)) return false;
            if (!round.TryApply(hit, attack, m_Combo, m_Damage, Time.unscaledTime, UnityEngine.Random.value, out var outcome)) return false;
            m_FeedbackPoint = hit.viewportPoint;
            m_FeedbackColor = outcome.Critical ? new Color(1f, .45f, .18f) : Color.white;
            m_Feedback = (outcome.Critical ? "暴击 " : "") + "−" + outcome.AppliedDamage.ToString("0.#") +
                "   COMBO " + outcome.ComboCount + "  ×" + outcome.ComboMultiplier.ToString("0.00") +
                (outcome.Milestone ? " !" : "");
            m_FeedbackUntil = Time.unscaledTime + 1.1f;
            AttackResolved?.Invoke(outcome);
            if (round.Defeated)
            {
                m_Results.Enqueue(round);
                Defeated?.Invoke(round);
            }
            return true;
        }

        void OnInteraction(PersonInteractionHit hit) => Confirm(hit, true);

        void Update()
        {
            if (!m_Active || m_Tracking == null) return;
            SyncPeople();
            foreach (var round in m_Rounds.Values) round.Tick(Time.unscaledTime, m_Combo);
            if (m_Result == null && m_Results.Count > 0 && Time.unscaledTime >= m_FeedbackUntil)
            {
                m_Result = m_Results.Dequeue();
                CancelGesture(); m_Surface.EndPointer(); m_Thrower.CancelGesture();
            }
            if (!BodyPointerFrame.TryRead(out var frame)) { CancelGesture(); return; }
            ProcessPointer(frame);
        }

        void SyncPeople()
        {
            m_Expired.Clear();
            foreach (var pair in m_Rounds) if (m_Tracking.Find(pair.Key) == null) m_Expired.Add(pair.Key);
            foreach (int id in m_Expired) m_Rounds.Remove(id);
            foreach (var person in m_Tracking.Persons) CaptureRound(person.personId);
        }

        public void ProcessPointer(BodyPointerFrame frame)
        {
            if (!m_Active || BlocksInput || ARCoreModeOverlay.WheelControlsPersonInput || ARCoreModeOverlay.IsProjectilePickerOpen ||
                m_Tracking == null || m_Camera == null) { CancelGesture(); return; }
            if (frame.down)
            {
                CancelGesture();
                if (m_UiFrame == Time.frameCount || ARCoreModeOverlay.IsPointOnOverlay(frame.position)) return;
                if (m_Feature != GameMode.Deform && !(m_Feature == GameMode.Throw && m_Holding)) return;
                m_Pressed = true; m_Pointer = frame.id; m_Origin = m_Previous = frame.position;
                m_PressTime = Time.unscaledTime;
                var person = Pick(frame.position);
                m_PressPerson = person != null ? person.personId : -1;
                if (!m_Holding && m_PressPerson >= 0)
                {
                    m_Surface.RefreshRegion(0f);
                    m_Surface.TryBegin(frame.id, frame.position);
                }
                return;
            }
            if (!m_Pressed || frame.id != m_Pointer) return;
            if (frame.canceled || ARCoreModeOverlay.IsPointOnOverlay(frame.position)) { CancelGesture(); return; }
            if (m_Holding)
            {
                m_HoldPoint = frame.position;
                if (frame.held || frame.released) Sweep(m_Previous, frame.position, true);
            }
            else if (Vector2.Distance(frame.position, m_Origin) > SwipeThreshold || m_Swiping)
            {
                if (!m_Swiping) m_Surface.EndPointer();
                Vector2 from = m_Swiping ? m_Previous : m_Origin;
                m_Swiping = true;
                Sweep(from, frame.position, false);
            }
            else if (frame.held) m_Surface.Warp.DragAt(frame.id, WorldPoint(frame.position));
            m_Previous = frame.position;
            if (!frame.released) return;
            if (!m_Holding && !m_Swiping && Time.unscaledTime - m_PressTime < .5f)
            {
                var person = Pick(frame.position);
                if (person != null && person.personId == m_PressPerson)
                    Report(person.personId, "press", ToViewport(frame.position));
            }
            CancelGesture();
        }

        Vector3 WorldPoint(Vector2 screen)
        {
            var warp = m_Surface.Warp;
            var ray = m_Camera.ScreenPointToRay(screen);
            return new Plane(warp.transform.forward, warp.transform.position).Raycast(ray, out float distance)
                ? ray.GetPoint(distance) : warp.transform.position;
        }

        public int Sweep(Vector2 fromScreen, Vector2 toScreen, bool holding)
        {
            int count = 0;
            Vector2 from = ToViewport(fromScreen), to = ToViewport(toScreen);
            foreach (var person in m_Tracking.Persons)
            {
                if (person.trackingConfidence < .2f || !SegmentRect(from, to, person.screenRect, out var point)) continue;
                if (holding)
                {
                    if (m_HoldHits.TryGetValue(person.personId, out float last) && Time.unscaledTime - last < HoldInterval) continue;
                    m_HoldHits[person.personId] = Time.unscaledTime;
                }
                else if (!m_GestureHits.Add(person.personId)) continue;
                if (Report(person.personId, holding ? "StickyHandHold" : "swipe", point))
                {
                    count++;
                    m_Surface.PulseAt(person.personId, m_Camera.ViewportToScreenPoint(point), toScreen - fromScreen);
                }
            }
            return count;
        }

        bool Report(int id, string action, Vector2 point)
        {
            int token = CaptureRound(id);
            return token != 0 && Confirm(new PersonInteractionHit(id, action, point, PersonInteractionHit.NextId(), token));
        }

        PersonPoseData Pick(Vector2 screen) => m_Tracking.Pick(ToViewport(screen));
        Vector2 ToViewport(Vector2 screen) => m_Camera.ScreenToViewportPoint(screen);

        public static bool SegmentRect(Vector2 from, Vector2 to, Rect rect, out Vector2 point)
        {
            float enter = 0f, exit = 1f;
            Vector2 delta = to - from;
            bool hit = Clip(-delta.x, from.x - rect.xMin, ref enter, ref exit) &&
                Clip(delta.x, rect.xMax - from.x, ref enter, ref exit) &&
                Clip(-delta.y, from.y - rect.yMin, ref enter, ref exit) &&
                Clip(delta.y, rect.yMax - from.y, ref enter, ref exit);
            point = Vector2.Lerp(from, to, enter);
            return hit;
        }

        static bool Clip(float p, float q, ref float enter, ref float exit)
        {
            if (Mathf.Abs(p) < .000001f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > exit) return false; enter = Mathf.Max(enter, r); }
            else { if (r < enter) return false; exit = Mathf.Min(exit, r); }
            return true;
        }

        void CancelGesture()
        {
            if (m_Pressed) m_Surface?.EndPointer();
            m_Pressed = false; m_Swiping = false; m_PressPerson = -1;
            m_GestureHits.Clear(); m_HoldHits.Clear();
        }

        public void SetHolding(bool holding)
        {
            CancelGesture();
            m_Holding = m_Active && m_Feature == GameMode.Throw && holding && m_Thrower.SelectedProjectileKey == "StickyHand";
            m_Surface.enabled = m_Feature == GameMode.Deform || m_Holding;
            m_HoldPoint = new Vector2(Screen.width * .5f, Screen.height * .65f);
            m_Thrower.CancelGesture(); ApplyInputPolicy();
        }

        public void RestartRound(int personId)
        {
            m_Rounds.Remove(personId);
            RoundRestarted?.Invoke(personId);
            CaptureRound(personId);
        }

        Rect ControlsRect
        {
            get
            {
                float scale = Mathf.Min(Screen.width / 1260f, Screen.height / 2732f);
                float pad = Mathf.Max(10f, 24f * scale);
                return new Rect((Screen.width - Mathf.Max(100f, 176f * scale)) * .5f,
                    Mathf.Max(pad, Screen.height - Screen.safeArea.yMax + pad),
                    Mathf.Max(100f, 176f * scale), Mathf.Max(44f, 76f * scale));
            }
        }
        float ButtonHeight => Mathf.Clamp(Screen.height * .045f, 60f, 98f);
        public bool IsPointOnControls(Vector2 guiPoint) => m_Active && (BlocksInput || ControlsRect.Contains(guiPoint));

        public void DrawOverlay(bool presenting)
        {
            if (!m_Active) return;
            var text = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * .022f, 17f, 28f)),
                alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            if (!presenting)
            {
                foreach (var round in m_Rounds.Values) DrawHealth(round, text);
                if (m_Feedback != null && Time.unscaledTime < m_FeedbackUntil)
                {
                    Vector3 screen = m_Camera.ViewportToScreenPoint(m_FeedbackPoint);
                    text.normal.textColor = m_FeedbackColor;
                    float width = Mathf.Min(Screen.width * .85f, 650f);
                    GUI.Label(new Rect(Mathf.Clamp(screen.x - width * .5f, 4f, Screen.width - width),
                        Mathf.Clamp(Screen.height - screen.y - 70f, Screen.height * .15f, Screen.height * .75f), width, 65f), m_Feedback, text);
                    text.normal.textColor = Color.white;
                }
            }
            DrawControls(text);
            if (m_Holding && m_Pressed && m_Thrower.SelectedProjectilePreview != null)
                GUI.DrawTexture(new Rect(m_HoldPoint.x - 65f, Screen.height - m_HoldPoint.y - 65f, 130f, 130f),
                    m_Thrower.SelectedProjectilePreview, ScaleMode.ScaleToFit, true);
            if (m_Settings || m_Result != null) DrawModal(text);
        }

        void DrawHealth(CombatRound round, GUIStyle text)
        {
            var person = m_Tracking.Find(round.PersonId);
            if (person == null || person.trackingConfidence < .2f) return;
            Vector3 top = m_Camera.ViewportToScreenPoint(new Vector3(person.screenRect.center.x, person.screenRect.yMax, 0));
            float width = Mathf.Clamp(person.screenRect.width * Screen.width, 140f, Screen.width * .40f);
            var rect = new Rect(Mathf.Clamp(top.x - width * .5f, 4f, Screen.width - width - 4f),
                Mathf.Max(Screen.height * .115f, Screen.height - top.y - 42f), width, 22f);
            Color old = GUI.color;
            GUI.color = new Color(.08f, .10f, .12f, .9f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = round.Defeated ? Color.gray : new Color(.25f, .85f, .42f);
            GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, (rect.width - 4) * round.CurrentHealth / round.MaxHealth, rect.height - 4), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(rect.x, rect.y - 30f, rect.width, 30f),
                "人物 " + round.PersonId + "  " + round.CurrentHealth.ToString("0") + "/" + round.MaxHealth.ToString("0"), text);
        }

        void DrawControls(GUIStyle text)
        {
            Rect area = ControlsRect;
            var button = new GUIStyle(GUI.skin.button) { fontSize = text.fontSize, wordWrap = true };
            if (GUI.Button(area, "战斗设置", button))
            {
                m_Settings = true; m_UiFrame = Time.frameCount;
                CancelGesture(); m_Surface.EndPointer(); m_Thrower.CancelGesture();
            }
        }

        void DrawModal(GUIStyle text)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, .82f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = old;
            float width = Mathf.Min(Screen.width * .86f, 780f);
            float x = (Screen.width - width) * .5f, y = Screen.height * .22f;
            var button = new GUIStyle(GUI.skin.button) { fontSize = text.fontSize };
            text.wordWrap = true;
            if (m_Result != null)
            {
                GUI.Label(new Rect(x, y, width, 60), "人物 " + m_Result.PersonId + " · 战斗结算", text);
                GUI.Label(new Rect(x, y + 65f, width, 90), "攻击 " + m_Result.History.Count + " 次    最高连击 " + m_Result.BestCombo +
                    "\n暴击 " + m_Result.CriticalCount + " 次    有效伤害 " + m_Result.TotalDamage.ToString("0.#"), text);
                var counts = new Dictionary<string, int>();
                foreach (var record in m_Result.History)
                { string name = record.Definition.DisplayName + record.Definition.ItemName; counts[name] = counts.TryGetValue(name, out int count) ? count + 1 : 1; }
                string summary = "";
                foreach (var pair in counts) summary += pair.Key + " ×" + pair.Value + "   ";
                GUI.Label(new Rect(x, y + 170f, width, Screen.height * .22f), summary, text);
                float by = Mathf.Min(Screen.height - ButtonHeight * 2.8f, y + Screen.height * .40f);
                if (GUI.Button(new Rect(x, by, width * .48f, ButtonHeight), "继续其他人物", button))
                { m_Result = null; m_UiFrame = Time.frameCount; }
                if (GUI.Button(new Rect(x + width * .52f, by, width * .48f, ButtonHeight), "此人物重新开始", button))
                { int person = m_Result.PersonId; m_Result = null; RestartRound(person); m_UiFrame = Time.frameCount; }
            }
            else
            {
                GUI.Label(new Rect(x, y, width, 70), "新回合血量（当前回合不变）", text);
                string[] labels = { "低", "中", "高" };
                for (int i = 0; i < 3; i++)
                {
                    int tier = i;
                    if (GUI.Button(new Rect(x + i * width / 3f, y + 80, width / 3f - 5, ButtonHeight),
                        labels[i] + (m_HealthTier == i ? " ✓" : ""), button)) m_HealthTier = tier;
                }
                if (GUI.Button(new Rect(x, y + 110f + ButtonHeight, width, ButtonHeight), "关闭", button))
                { m_Settings = false; m_UiFrame = Time.frameCount; }
            }
        }

        void OnDestroy()
        {
            if (m_Thrower != null)
            { m_Thrower.InteractionConfirmed -= OnInteraction; m_Thrower.InteractionContext = null; m_Thrower.ReadPointerInput = true; }
            if (m_Surface != null) m_Surface.ReadPointerInput = true;
            foreach (var value in m_Owned)
                if (value != null) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        }
    }
}
