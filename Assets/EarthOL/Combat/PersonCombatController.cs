using System;
using System.Collections;
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
        SwipeDeformController m_SwipeDeform;
        BodyProjectileCatalog m_Catalog;
        ComboConfig m_Combo;
        AttackDamageConfig m_Damage;
        CombatConfig m_Config;
        CombatSnapshotCapture m_Snapshots;
        Vector2 m_ResultScroll;
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
        int m_Pointer, m_PressPerson = -1, m_NextToken, m_Session, m_HealthTier = 1, m_SelectedPersonId = -1;
        Vector2 m_Origin, m_Previous, m_HoldPoint;
        float m_PressTime, m_FeedbackUntil;
        string m_Feedback;
        Vector2 m_FeedbackPoint;
        Color m_FeedbackColor;
        int m_UiFrame = -1;
        const float SwipeThreshold = 40f;
        const float HoldInterval = .35f;
        readonly Dictionary<int, HealthBarAnim> m_HealthAnims = new Dictionary<int, HealthBarAnim>();

        public bool Active => m_Active;
        public bool BlocksInput => m_Active && (m_Settings || m_Result != null);
        public bool Holding => m_Holding;
        public bool CanHoldSelectedItem => m_Thrower != null && m_Thrower.SelectedEntry != null && m_Thrower.SelectedEntry.canHold;
        public bool IsSelecting => m_Active && m_SelectedPersonId < 0;
        public int SelectedPersonId => m_SelectedPersonId;
        public int Session => m_Session;
        public IReadOnlyDictionary<int, CombatRound> Rounds => m_Rounds;
        public event Action<AttackOutcome> AttackResolved;
        public event Action<CombatRound> Defeated;
        public event Action<int> RoundRestarted;
        public event Action ContinueOtherPersons;

        public void Configure(Camera camera, PersonTrackingManager tracking,
            PersonDeformationSurface surface, Person3DThrowController thrower, SwipeDeformController swipeDeform)
        {
            m_Camera = camera; m_Tracking = tracking; m_Surface = surface; m_Thrower = thrower; m_SwipeDeform = swipeDeform;
            m_Snapshots = GetComponent<CombatSnapshotCapture>();
            if (m_Snapshots == null) m_Snapshots = gameObject.AddComponent<CombatSnapshotCapture>();
            m_Catalog = Resources.Load<BodyProjectileCatalog>("Throwables/Catalog");
            m_Config = Resources.Load<CombatConfig>("Combat/CombatConfig");
            m_Combo = m_Config != null && m_Config.comboConfig != null ? m_Config.comboConfig : Own<ComboConfig>();
            m_Damage = m_Config != null && m_Config.attackDamageConfig != null ? m_Config.attackDamageConfig : Own<AttackDamageConfig>();
            Register("press", "按压", "", 10f);
            Register("swipe", "挥扫", "", 8f);
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
            m_SelectedPersonId = -1;
            m_Snapshots?.Clear(); m_ResultScroll = Vector2.zero;
            m_Rounds.Clear(); m_Result = null; m_Results.Clear(); m_Settings = false;
            m_Feedback = null; m_FeedbackUntil = 0;
            foreach (var anim in m_HealthAnims.Values)
                if (anim.routine != null) StopCoroutine(anim.routine);
            m_HealthAnims.Clear();
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
            if (m_Surface != null) m_Surface.ReadPointerInput = !m_Active || (!m_Holding && m_Feature == GameMode.Deform && !IsSelecting);
            if (m_Thrower != null) m_Thrower.ReadPointerInput = false;
        }

        public void RefreshInputPolicy() => ApplyInputPolicy();

        float NewRoundHealth()
        {
            if (m_Config != null && m_Config.healthTiers != null && m_Config.healthTiers.Length > m_HealthTier)
                return Mathf.Max(1f, m_Config.healthTiers[m_HealthTier]);
            return m_HealthTier == 0 ? 60f : m_HealthTier == 2 ? 240f : 120f;
        }

        public CombatRound FindRound(int personId) => m_Rounds.TryGetValue(personId, out var round) ? round : null;

        public int CaptureRound(int personId)
        {
            if (!m_Active || m_Tracking == null || m_SelectedPersonId < 0 || personId != m_SelectedPersonId) return 0;
            var person = m_Tracking.Find(personId);
            if (person == null || !person.IsValid || person.trackingConfidence < .2f) return 0;
            if (!m_Rounds.TryGetValue(personId, out var round))
            {
                round = new CombatRound(personId, ++m_NextToken, NewRoundHealth());
                m_Rounds.Add(personId, round);
                if (m_Snapshots != null) round.Snapshot = m_Snapshots.Schedule(personId,round.Token,m_Session);
            }
            return round.Defeated ? 0 : round.Token;
        }

        public bool CaptureDefeatedPersonSticker(int personId, int session)
        {
            if (!m_Active || session != m_Session || !m_Rounds.TryGetValue(personId,out var round) || !round.Defeated)
                return false;
            return m_Snapshots != null && m_Snapshots.CaptureOnStickerDisplayed(round.Snapshot,personId,session);
        }

        public void SelectPerson(int personId)
        {
            if (!m_Active || m_Tracking == null || m_SelectedPersonId >= 0) return;
            var person = m_Tracking.Find(personId);
            if (person == null || !person.IsValid || person.trackingConfidence < .2f) return;
            m_SelectedPersonId = personId;
            CaptureRound(personId);
            ApplyInputPolicy();
        }

        public bool Confirm(PersonInteractionHit hit) => Confirm(hit, false);

        bool Confirm(PersonInteractionHit hit, bool inFlight)
        {
            if (!m_Active || BlocksInput && !inFlight || !m_Attacks.TryGetValue(hit.actionId ?? "", out var attack)) return false;
            var person = m_Tracking.Find(hit.personId);
            if (person == null || !person.IsValid || person.trackingConfidence < .2f ||
                !m_Rounds.TryGetValue(hit.personId, out var round)) return false;
            if (!round.TryApply(hit, attack, m_Combo, m_Damage, Time.unscaledTime, UnityEngine.Random.value, out var outcome)) return false;
            NotifyHealthDamaged(round);
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
            TryShowNextResult();
            if (!BodyPointerFrame.TryRead(out var frame)) { CancelGesture(); return; }
            ProcessPointer(frame);
        }

        void TryShowNextResult()
        {
            if (m_Result == null && m_Results.Count > 0 && Time.unscaledTime >= m_FeedbackUntil &&
                !(m_Results.Peek().Snapshot?.IsPending ?? false))
            {
                m_Result = m_Results.Dequeue();
                m_ResultScroll = Vector2.zero;
                CancelGesture(); m_Surface?.EndPointer(); m_Thrower?.CancelGesture();
            }
        }

        void SyncPeople()
        {
            m_Expired.Clear();
            foreach (var pair in m_Rounds) if (m_Tracking.Find(pair.Key) == null) m_Expired.Add(pair.Key);
            foreach (int id in m_Expired)
            {
                var round = m_Rounds[id];
                m_Snapshots?.CancelPending(round.Snapshot);
                if (ReferenceEquals(round,m_Result) || m_Results.Contains(round)) continue;
                m_Snapshots?.Release(round.Snapshot);
                m_Rounds.Remove(id);
                RemoveHealthAnim(id);
            }
            if (m_SelectedPersonId >= 0 && m_Tracking.Find(m_SelectedPersonId) == null && m_Result == null && m_Results.Count == 0)
            {
                m_SelectedPersonId = -1;
                CancelGesture();
                ApplyInputPolicy();
            }
            if (m_SelectedPersonId >= 0) CaptureRound(m_SelectedPersonId);
        }

        public void ProcessPointer(BodyPointerFrame frame)
        {
            if (!m_Active || BlocksInput || (ARCoreModeOverlay.WheelTapToThrowEnabled && !m_Holding && !IsSelecting) || ARCoreModeOverlay.IsProjectilePickerOpen ||
                m_Tracking == null || m_Camera == null) { CancelGesture(); return; }

            if (m_Holding)
            {
                m_HoldPoint = frame.position;
                if (!m_Pressed) { m_Pressed = true; m_Pointer = frame.id; m_Previous = frame.position; }
                if (frame.id != m_Pointer) return;
                if (frame.canceled) { CancelGesture(); SetHolding(false); return; }
                if (frame.held || frame.released) Sweep(m_Previous, frame.position, true);
                m_Previous = frame.position;
                if (frame.released) { CancelGesture(); SetHolding(false); }
                return;
            }

            if (frame.down)
            {
                CancelGesture();
                if (m_UiFrame == Time.frameCount || ARCoreModeOverlay.IsPointOnOverlay(frame.position)) return;
                if (m_Feature != GameMode.Deform) return;
                m_Pressed = true; m_Pointer = frame.id; m_Origin = m_Previous = frame.position;
                m_PressTime = Time.unscaledTime;
                var person = Pick(frame.position);
                m_PressPerson = person != null ? person.personId : -1;
                return;
            }
            if (!m_Pressed || frame.id != m_Pointer) return;
            if (frame.canceled || ARCoreModeOverlay.IsPointOnOverlay(frame.position)) { CancelGesture(); return; }
            if (Vector2.Distance(frame.position, m_Origin) > SwipeThreshold || m_Swiping)
            {
                if (!m_Swiping) m_Surface.EndPointer();
                Vector2 from = m_Swiping ? m_Previous : m_Origin;
                m_Swiping = true;
                Sweep(from, frame.position, false);
            }
            m_Previous = frame.position;
            if (!frame.released) return;
            if (!m_Swiping && Time.unscaledTime - m_PressTime < .5f)
            {
                var person = Pick(frame.position);
                if (person != null && person.personId == m_PressPerson)
                {
                    if (m_SelectedPersonId < 0) SelectPerson(person.personId);
                    else Report(person.personId, "press", ToViewport(frame.position));
                }
            }
            CancelGesture();
        }

        public int Sweep(Vector2 fromScreen, Vector2 toScreen, bool holding)
        {
            int count = 0;
            Vector2 from = ToViewport(fromScreen), to = ToViewport(toScreen);
            Vector2 screenDelta = toScreen - fromScreen;
            Vector2 direction = screenDelta.sqrMagnitude > 1e-6f ? screenDelta.normalized : Vector2.right;
            float speed = screenDelta.magnitude / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            foreach (var person in m_Tracking.Persons)
            {
                if (person.personId != m_SelectedPersonId) continue;
                if (person.trackingConfidence < .2f || !SegmentRect(from, to, person.screenRect, out var point)) continue;
                if (holding)
                {
                    if (person.screenRect.Contains(from)) continue;
                    if (m_HoldHits.TryGetValue(person.personId, out float last) && Time.unscaledTime - last < HoldInterval) continue;
                    m_HoldHits[person.personId] = Time.unscaledTime;
                }
                else if (!m_GestureHits.Add(person.personId)) continue;
                if (Report(person.personId, "swipe", point))
                {
                    count++;
                    if (m_SwipeDeform != null)
                    {
                        m_Surface.FocusPerson(person.personId);
                        m_SwipeDeform.Trigger(direction, speed, m_Camera.ViewportToScreenPoint(point));
                    }
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
            m_Holding = m_Active && holding && m_Thrower != null && m_Thrower.SelectedProjectileKey == "StickyHand";
            m_Surface.enabled = m_Feature == GameMode.Deform || m_Holding;
            m_HoldPoint = new Vector2(Screen.width * .5f, Screen.height * .65f);
            if (m_Thrower != null) m_Thrower.CancelGesture();
            ApplyInputPolicy();
        }

        public void RestartRound(int personId)
        {
            if (m_Rounds.TryGetValue(personId,out var previous)) m_Snapshots?.Release(previous.Snapshot);
            m_Rounds.Remove(personId);
            RemoveHealthAnim(personId);
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
                if (IsSelecting) DrawSelection(text);
                else
                {
                    DrawPersonBox(m_Tracking.Find(m_SelectedPersonId));
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
            }
            DrawControls(text);
            if (m_Holding && m_Pressed && m_Thrower.SelectedProjectilePreview != null)
            {
                float heldSize = Mathf.Clamp(Screen.width * .13f, 96f, 190f);
                GUI.DrawTexture(new Rect(m_HoldPoint.x - heldSize * .5f, Screen.height - m_HoldPoint.y - heldSize * .5f, heldSize, heldSize),
                    m_Thrower.SelectedProjectilePreview, ScaleMode.ScaleToFit, true);
            }
            if (m_Settings || m_Result != null) DrawModal(text);
        }

        void DrawHealth(CombatRound round, GUIStyle text)
        {
            var person = m_Tracking.Find(round.PersonId);
            if (person == null || person.trackingConfidence < .2f) return;
            var anim = GetHealthAnim(round.PersonId);
            Vector3 top = m_Camera.ViewportToScreenPoint(new Vector3(person.screenRect.center.x, person.screenRect.yMax, 0));
            float width = Mathf.Clamp(person.screenRect.width * Screen.width, 140f, Screen.width * .40f);
            float baseX = Mathf.Clamp(top.x - width * .5f, 4f, Screen.width - width - 4f);
            float baseY = Mathf.Max(Screen.height * .115f, Screen.height - top.y - 42f);
            var rect = new Rect(baseX + anim.shakeOffset.x, baseY + anim.shakeOffset.y, width, 22f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, .95f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            var bg = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            GUI.color = new Color(.06f, .06f, .07f, .85f); GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = new Color(.88f, .12f, .10f, 1f);
            GUI.DrawTexture(new Rect(bg.x, bg.y, bg.width * Mathf.Clamp01(anim.displayedFill), bg.height), Texture2D.whiteTexture);
            if (anim.flashVisible)
            {
                float alpha = Mathf.FloorToInt((Time.unscaledTime - anim.flashStart) * 20f) % 2 == 0 ? .95f : .15f;
                GUI.color = new Color(1f, .88f, .05f, alpha);
                GUI.DrawTexture(new Rect(bg.x + bg.width * anim.flashTo, bg.y, bg.width * (anim.flashFrom - anim.flashTo), bg.height), Texture2D.whiteTexture);
            }
            GUI.color = old;
            GUI.Label(new Rect(baseX, baseY - 30f, width, 30f),
                "人物 " + round.PersonId + "  " + round.CurrentHealth.ToString("0") + "/" + round.MaxHealth.ToString("0"), text);
        }

        void DrawSelection(GUIStyle text)
        {
            foreach (var person in m_Tracking.Persons) DrawPersonBox(person);
        }

        void DrawPersonBox(PersonPoseData person)
        {
            if (person == null || person.trackingConfidence < .2f || m_Camera == null) return;
            Vector3 vmin = m_Camera.ViewportToScreenPoint(new Vector3(person.screenRect.xMin, person.screenRect.yMin, 0));
            Vector3 vmax = m_Camera.ViewportToScreenPoint(new Vector3(person.screenRect.xMax, person.screenRect.yMax, 0));
            Rect box = Rect.MinMaxRect(Mathf.Min(vmin.x, vmax.x), Screen.height - Mathf.Max(vmin.y, vmax.y),
                Mathf.Max(vmin.x, vmax.x), Screen.height - Mathf.Min(vmin.y, vmax.y));
            DrawBoxOutline(box, 3f, new Color(.35f, .78f, .72f, .9f));
        }

        HealthBarAnim GetHealthAnim(int id)
        {
            if (!m_HealthAnims.TryGetValue(id, out var anim)) m_HealthAnims[id] = anim = new HealthBarAnim();
            return anim;
        }

        void RemoveHealthAnim(int id)
        {
            if (!m_HealthAnims.TryGetValue(id, out var anim)) return;
            if (anim.routine != null) StopCoroutine(anim.routine);
            m_HealthAnims.Remove(id);
        }

        void NotifyHealthDamaged(CombatRound round)
        {
            var anim = GetHealthAnim(round.PersonId);
            float target = Mathf.Clamp01(round.CurrentHealth / round.MaxHealth);
            anim.pending.Enqueue((anim.targetFill, target));
            anim.targetFill = target;
            if (anim.routine == null) anim.routine = StartCoroutine(PlayHealthAnim(anim));
        }

        IEnumerator PlayHealthAnim(HealthBarAnim anim)
        {
            while (anim.pending.Count > 0)
            {
                var segment = anim.pending.Dequeue();
                float elapsed = 0f;
                while (elapsed < .10f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    anim.shakeOffset = UnityEngine.Random.insideUnitCircle * (Mathf.Clamp(Screen.width * .012f, 6f, 14f) * (1f - Mathf.Clamp01(elapsed / .10f)));
                    yield return null;
                }
                anim.shakeOffset = Vector2.zero;
                anim.flashFrom = segment.from; anim.flashTo = segment.to;
                anim.flashStart = Time.unscaledTime; anim.flashVisible = true;
                elapsed = 0f;
                while (elapsed < .16f) { elapsed += Time.unscaledDeltaTime; yield return null; }
                anim.flashVisible = false;
                elapsed = 0f;
                while (elapsed < .14f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    anim.displayedFill = Mathf.Lerp(segment.from, segment.to, Mathf.Clamp01(elapsed / .14f));
                    yield return null;
                }
                anim.displayedFill = segment.to;
            }
            anim.routine = null;
        }

        sealed class HealthBarAnim
        {
            public float displayedFill = 1f, targetFill = 1f;
            public Vector2 shakeOffset;
            public bool flashVisible;
            public float flashFrom, flashTo, flashStart;
            public readonly Queue<(float from, float to)> pending = new Queue<(float from, float to)>();
            public Coroutine routine;
        }

        void DrawBoxOutline(Rect rect, float thickness, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
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
                float by = Screen.height-Mathf.Max(Screen.safeArea.yMin,16f)-ButtonHeight-16f;
                DrawResultContent(new Rect(x,Mathf.Max(16f,Screen.height-Screen.safeArea.yMax+16f),width,
                    by-Mathf.Max(16f,Screen.height-Screen.safeArea.yMax+16f)-16f),text);
                if (GUI.Button(new Rect(x, by, width * .48f, ButtonHeight), "继续其他人物", button))
                { DismissResult(); m_UiFrame = Time.frameCount; ContinueOtherPersons?.Invoke(); }
                if (GUI.Button(new Rect(x + width * .52f, by, width * .48f, ButtonHeight), "此人物重新开始", button))
                { int person = m_Result.PersonId; DismissResult(); RestartRound(person); m_UiFrame = Time.frameCount; }
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

        void DismissResult()
        {
            if (m_Result != null) m_Snapshots?.Release(m_Result.Snapshot);
            m_Result = null;
            m_ResultScroll = Vector2.zero;
        }

        void DrawResultContent(Rect area, GUIStyle text)
        {
            float width = area.width-24f;
            var snapshot = m_Result.Snapshot;
            float photoHeight = Mathf.Min(Screen.height*.36f,width*.94f);
            var counts = new Dictionary<string,int>();
            foreach (var record in m_Result.History)
            {
                string name = record.Definition.DisplayName+record.Definition.ItemName;
                counts[name] = counts.TryGetValue(name,out int count) ? count+1 : 1;
            }
            string summary = "";
            foreach (var pair in counts) summary += pair.Key+" ×"+pair.Value+"   ";
            float heading = text.lineHeight*2, caption = text.lineHeight*2+12;
            string stats = "攻击 "+m_Result.History.Count+" 次    最高连击 "+m_Result.BestCombo+
                "\n暴击 "+m_Result.CriticalCount+" 次    有效伤害 "+m_Result.TotalDamage.ToString("0.#");
            float statsHeight = text.CalcHeight(new GUIContent(stats),width)+18;
            float summaryHeight = Mathf.Max(text.lineHeight*2,text.CalcHeight(new GUIContent(summary),width));
            m_ResultScroll = GUI.BeginScrollView(area,m_ResultScroll,
                new Rect(0,0,width,heading+photoHeight+caption+statsHeight+summaryHeight+20));
            GUI.Label(new Rect(0,0,width,heading),"人物 "+m_Result.PersonId+" · 战斗结算",text);
            var photoRect = new Rect(0,heading,width,photoHeight);
            Color old = GUI.color;
            GUI.color = new Color(.07f,.08f,.10f,1); GUI.DrawTexture(photoRect,Texture2D.whiteTexture); GUI.color = old;
            if (snapshot?.Image != null) GUI.DrawTexture(photoRect,snapshot.Image,ScaleMode.ScaleToFit,false);
            else GUI.Label(photoRect,snapshot?.State == CombatSnapshotState.Failed ? "本回合截图失败" : "本回合暂无截图",text);
            string status = snapshot?.Image == null ? "人物贴图截图" :
                string.IsNullOrEmpty(snapshot.SavedPath) ? "人物贴图截图 · 保存失败" : "人物贴图截图 · 已保存";
            float y = photoRect.yMax;
            GUI.Label(new Rect(0,y,width,caption),status,text); y += caption;
            GUI.Label(new Rect(0,y,width,statsHeight),stats,text); y += statsHeight;
            GUI.Label(new Rect(0,y,width,summaryHeight),summary,text);
            GUI.EndScrollView();
        }

        void OnDisable() => m_Snapshots?.Clear();

        void OnDestroy()
        {
            m_Snapshots?.Clear();
            if (m_Thrower != null)
            { m_Thrower.InteractionConfirmed -= OnInteraction; m_Thrower.InteractionContext = null; m_Thrower.ReadPointerInput = true; }
            if (m_Surface != null) m_Surface.ReadPointerInput = true;
            foreach (var value in m_Owned)
                if (value != null) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        }
    }
}
