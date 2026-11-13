using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FrontierStrike
{
    /// <summary>
    /// Game-loop HUD and menus, built entirely in code: health, wave, score, banners, hit markers,
    /// damage indicators, and the start / pause / end screens. Sits on top of the Infima HUD
    /// (crosshair + ammo), which stays as it is.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        private static readonly Color Accent = new Color(1.0f, 0.72f, 0.18f);
        private static readonly Color Danger = new Color(0.9f, 0.16f, 0.12f);
        private static readonly Color Healthy = new Color(0.42f, 0.85f, 0.38f);
        private static readonly Color PanelBg = new Color(0.03f, 0.04f, 0.03f, 0.82f);

        private RectTransform root;

        // In-game HUD
        private GameObject hudGroup;
        private RectTransform healthFill;
        private Image healthFillImage;
        private TextMeshProUGUI healthText, waveText, enemiesText, scoreText;
        private TextMeshProUGUI bannerTitle, bannerSub, popupText;
        private CanvasGroup bannerGroup, popupGroup;
        private Image damageOverlay, lowHealthOverlay;
        private Image[] hitMarker;
        private DamageIndicator[] indicators;

        // Menus
        private GameObject startPanel, pausePanel, endPanel;
        private TextMeshProUGUI startStatus, startBest, endTitle, endStats;
        private Button startButton;

        private float bannerUntil, popupUntil, hitMarkerUntil, damageAlpha;
        private float healthNormalized = 1.0f;

        private class DamageIndicator
        {
            public RectTransform Rect;
            public Image Image;
            public Vector3 SourcePosition;
            public float Until;
        }

        public event Action StartClicked, ResumeClicked, RestartClicked, QuitClicked;

        #region Build

        public static GameHUD Create()
        {
            EnsureEventSystem();

            var go = new GameObject("Frontier Strike HUD", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var hud = go.AddComponent<GameHUD>();
            hud.root = (RectTransform)go.transform;
            hud.Build();
            return hud;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        private void Build()
        {
            // Full-screen overlays first so everything else draws on top of them.
            lowHealthOverlay = FullScreenImage("Low Health", root, new Color(Danger.r, 0, 0, 0));
            damageOverlay = FullScreenImage("Damage Flash", root, new Color(Danger.r, 0, 0, 0));

            hudGroup = new GameObject("HUD", typeof(RectTransform));
            hudGroup.transform.SetParent(root, false);
            Stretch((RectTransform)hudGroup.transform);
            var hud = (RectTransform)hudGroup.transform;

            // Health (bottom-left)
            RectTransform healthPanel = Panel("Health", hud, new Vector2(0, 0), new Vector2(0, 0), new Vector2(48, 48), new Vector2(420, 64), new Color(0, 0, 0, 0.45f));
            Text(healthPanel, "HEALTH", 18, TextAlignmentOptions.TopLeft, new Vector2(14, -6), new Vector2(200, 24), Color.white * 0.8f);
            healthText = Text(healthPanel, "100", 30, TextAlignmentOptions.TopRight, new Vector2(-14, -2), new Vector2(160, 36), Color.white, anchorRight: true);
            RectTransform barBg = Panel("Bar BG", healthPanel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 12), new Vector2(-28, 14), new Color(1, 1, 1, 0.12f), stretchX: true);
            healthFill = Panel("Bar Fill", barBg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Healthy, stretchAll: true);
            healthFillImage = healthFill.GetComponent<Image>();

            // Wave + enemies (top-centre)
            waveText = Text(hud, "", 34, TextAlignmentOptions.Top, new Vector2(0, -28), new Vector2(600, 44), Accent, anchor: new Vector2(0.5f, 1));
            waveText.fontStyle = FontStyles.Bold;
            waveText.characterSpacing = 8;
            enemiesText = Text(hud, "", 22, TextAlignmentOptions.Top, new Vector2(0, -72), new Vector2(600, 30), Color.white * 0.9f, anchor: new Vector2(0.5f, 1));

            // Score (top-right)
            scoreText = Text(hud, "0", 34, TextAlignmentOptions.TopRight, new Vector2(-48, -28), new Vector2(500, 44), Color.white, anchor: new Vector2(1, 1));
            scoreText.fontStyle = FontStyles.Bold;

            // Centre banner
            var bannerRect = new GameObject("Banner", typeof(RectTransform)).GetComponent<RectTransform>();
            bannerRect.SetParent(hud, false);
            bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.72f);
            bannerRect.sizeDelta = new Vector2(1200, 160);
            bannerGroup = bannerRect.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0;
            bannerTitle = Text(bannerRect, "", 78, TextAlignmentOptions.Center, new Vector2(0, 26), new Vector2(1200, 96), Accent, anchor: new Vector2(0.5f, 0.5f));
            bannerTitle.fontStyle = FontStyles.Bold;
            bannerTitle.characterSpacing = 14;
            bannerSub = Text(bannerRect, "", 28, TextAlignmentOptions.Center, new Vector2(0, -44), new Vector2(1200, 40), Color.white, anchor: new Vector2(0.5f, 0.5f));

            // Kill popup under the crosshair
            popupText = Text(hud, "", 28, TextAlignmentOptions.Center, new Vector2(0, -70), new Vector2(600, 40), Accent, anchor: new Vector2(0.5f, 0.5f));
            popupText.fontStyle = FontStyles.Bold;
            popupGroup = popupText.gameObject.AddComponent<CanvasGroup>();
            popupGroup.alpha = 0;

            // Hit marker: four short diagonal strokes around the crosshair.
            hitMarker = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                float angle = 45.0f + 90.0f * i;
                Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                RectTransform stroke = Panel("Hit Marker", hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dir * 16.0f, new Vector2(14, 3), Color.white, centered: true);
                stroke.localRotation = Quaternion.Euler(0, 0, angle);
                hitMarker[i] = stroke.GetComponent<Image>();
                hitMarker[i].enabled = false;
            }

            // Directional damage indicators.
            indicators = new DamageIndicator[4];
            for (int i = 0; i < indicators.Length; i++)
            {
                RectTransform r = Panel("Damage Indicator", hud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90, 12), Danger, centered: true);
                var img = r.GetComponent<Image>();
                img.enabled = false;
                indicators[i] = new DamageIndicator { Rect = r, Image = img };
            }

            BuildStartPanel();
            BuildPausePanel();
            BuildEndPanel();
        }

        private void BuildStartPanel()
        {
            startPanel = MenuPanel("Start Menu", out RectTransform content);
            MenuTitle(content, "FRONTIER STRIKE", 96, Accent);
            MenuText(content, "Hold the military base. Survive 5 waves of hostile soldiers.", 30, Color.white);
            MenuSpacer(content, 18);
            MenuText(content,
                "<b>WASD</b> move   <b>Shift</b> sprint   <b>Mouse</b> look\n" +
                "<b>LMB</b> fire   <b>RMB</b> aim   <b>R</b> reload   <b>Q / Wheel</b> switch weapon\n" +
                "<b>Esc</b> pause   <b>Tab</b> show controls\n\n" +
                "Headshots deal double damage. Enemies may drop medkits.",
                24, new Color(1, 1, 1, 0.8f));
            MenuSpacer(content, 24);
            startButton = MenuButton(content, "DEPLOY", () => StartClicked?.Invoke());
            startStatus = MenuText(content, "", 22, new Color(1, 1, 1, 0.6f));
            startBest = MenuText(content, "", 22, Accent);
        }

        private void BuildPausePanel()
        {
            pausePanel = MenuPanel("Pause Menu", out RectTransform content);
            MenuTitle(content, "PAUSED", 80, Accent);
            MenuSpacer(content, 20);
            MenuButton(content, "RESUME", () => ResumeClicked?.Invoke());
            MenuButton(content, "RESTART", () => RestartClicked?.Invoke());
            AddQuitButton(content);
        }

        private void BuildEndPanel()
        {
            endPanel = MenuPanel("End Menu", out RectTransform content);
            endTitle = MenuTitle(content, "", 88, Accent);
            endStats = MenuText(content, "", 28, Color.white);
            MenuSpacer(content, 20);
            MenuButton(content, "PLAY AGAIN", () => RestartClicked?.Invoke());
            AddQuitButton(content);
        }

        private void AddQuitButton(RectTransform content)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            MenuButton(content, "QUIT", () => QuitClicked?.Invoke());
#endif
        }

        #endregion

        #region Public API

        public void SetHealth(float current, float max)
        {
            healthNormalized = max > 0 ? Mathf.Clamp01(current / max) : 0;
            healthFill.anchorMax = new Vector2(healthNormalized, 1);
            healthFillImage.color = healthNormalized > 0.5f ? Healthy : Color.Lerp(Danger, Accent, healthNormalized * 2.0f);
            healthText.text = Mathf.CeilToInt(current).ToString();
        }

        public void SetWave(int wave, int total) => waveText.text = wave > 0 ? $"WAVE {wave} / {total}" : "";

        public void SetEnemies(string text) => enemiesText.text = text;

        public void SetScore(int score) => scoreText.text = score.ToString("N0");

        public void SetHudVisible(bool visible) => hudGroup.SetActive(visible);

        public void ShowBanner(string title, string subtitle, float duration = 2.5f)
        {
            bannerTitle.text = title;
            bannerSub.text = subtitle;
            bannerUntil = Time.unscaledTime + duration;
            bannerGroup.alpha = 1;
        }

        public void ShowPopup(string text, Color color)
        {
            popupText.text = text;
            popupText.color = color;
            popupUntil = Time.unscaledTime + 1.0f;
            popupGroup.alpha = 1;
        }

        public void ShowHitMarker(bool kill, bool headshot)
        {
            Color c = kill ? Danger : (headshot ? Accent : Color.white);
            foreach (Image img in hitMarker)
            {
                img.enabled = true;
                img.color = c;
                img.rectTransform.sizeDelta = new Vector2(kill ? 20 : 14, 3);
            }
            hitMarkerUntil = Time.unscaledTime + (kill ? 0.35f : 0.15f);
        }

        public void ShowDamage(Vector3 sourcePosition, float amount)
        {
            damageAlpha = Mathf.Clamp(damageAlpha + amount / 60.0f, 0.12f, 0.45f);

            DamageIndicator slot = indicators[0];
            foreach (DamageIndicator d in indicators)
                if (d.Until < slot.Until)
                    slot = d;
            slot.SourcePosition = sourcePosition;
            slot.Until = Time.unscaledTime + 1.2f;
            slot.Image.enabled = true;
        }

        public void ShowStart(int bestScore)
        {
            startPanel.SetActive(true);
            startBest.text = bestScore > 0 ? $"BEST SCORE  {bestScore:N0}" : "";
        }

        public void SetStartReady(bool ready, string status)
        {
            startButton.interactable = ready;
            startStatus.text = status;
        }

        public void HideStart() => startPanel.SetActive(false);

        public void ShowPause(bool show) => pausePanel.SetActive(show);

        public void ShowEnd(bool victory, string stats)
        {
            endPanel.SetActive(true);
            endTitle.text = victory ? "MISSION COMPLETE" : "MISSION FAILED";
            endTitle.color = victory ? Accent : Danger;
            endStats.text = stats;
        }

        #endregion

        #region Update

        private void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            if (bannerGroup.alpha > 0 && now > bannerUntil)
                bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, 0, dt * 2.5f);
            if (popupGroup.alpha > 0 && now > popupUntil)
                popupGroup.alpha = Mathf.MoveTowards(popupGroup.alpha, 0, dt * 3.0f);

            if (now > hitMarkerUntil && hitMarker[0].enabled)
                foreach (Image img in hitMarker)
                    img.enabled = false;

            damageAlpha = Mathf.MoveTowards(damageAlpha, 0, dt * 0.9f);
            damageOverlay.color = new Color(Danger.r, 0.0f, 0.0f, damageAlpha);

            // Pulsing red tint while below 30% health.
            float low = Mathf.InverseLerp(0.3f, 0.0f, healthNormalized);
            float pulse = low > 0 ? (0.6f + 0.4f * Mathf.Sin(now * 5.0f)) : 0;
            lowHealthOverlay.color = new Color(0.5f, 0.0f, 0.0f, low * 0.28f * pulse);

            UpdateIndicators(now);
        }

        private void UpdateIndicators(float now)
        {
            GameManager gm = GameManager.Instance;
            Camera cam = gm != null && gm.Player != null ? gm.Player.GetCameraWorld() : null;

            foreach (DamageIndicator d in indicators)
            {
                if (!d.Image.enabled)
                    continue;
                if (now > d.Until || cam == null)
                {
                    d.Image.enabled = false;
                    continue;
                }

                Vector3 forward = cam.transform.forward;
                forward.y = 0;
                Vector3 toSource = d.SourcePosition - cam.transform.position;
                toSource.y = 0;
                float angle = Vector3.SignedAngle(forward, toSource, Vector3.up);

                const float radius = 170.0f;
                float rad = angle * Mathf.Deg2Rad;
                d.Rect.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * radius;
                d.Rect.localRotation = Quaternion.Euler(0, 0, -angle);
                Color c = Danger;
                c.a = Mathf.Clamp01((d.Until - now) / 0.6f);
                d.Image.color = c;
            }
        }

        #endregion

        #region UI Helpers

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private static Image FullScreenImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color color,
            bool stretchX = false, bool stretchAll = false, bool centered = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            if (stretchAll)
                Stretch(r);
            else
            {
                r.pivot = centered ? new Vector2(0.5f, 0.5f) : new Vector2(0, 0);
                r.anchoredPosition = position;
                r.sizeDelta = size;
            }
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return r;
        }

        private static TextMeshProUGUI Text(Transform parent, string text, float size, TextAlignmentOptions align, Vector2 position, Vector2 boxSize, Color color,
            bool anchorRight = false, Vector2? anchor = null)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            Vector2 a = anchor ?? (anchorRight ? new Vector2(1, 1) : new Vector2(0, 1));
            r.anchorMin = r.anchorMax = a;
            r.pivot = a;
            r.anchoredPosition = position;
            r.sizeDelta = boxSize;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private GameObject MenuPanel(string name, out RectTransform content)
        {
            Image bg = FullScreenImage(name, root, PanelBg);
            bg.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(bg.transform, false);
            content = (RectTransform)contentGo.transform;
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(1200, 900);

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            bg.gameObject.SetActive(false);
            return bg.gameObject;
        }

        private static TextMeshProUGUI MenuText(Transform parent, string text, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.lineSpacing = 12;
            return t;
        }

        private static TextMeshProUGUI MenuTitle(Transform parent, string text, float size, Color color)
        {
            TextMeshProUGUI t = MenuText(parent, text, size, color);
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 16;
            return t;
        }

        private static void MenuSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = height;
        }

        private static Button MenuButton(Transform parent, string label, Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 360;
            le.minHeight = le.preferredHeight = 64;

            var img = go.GetComponent<Image>();
            img.color = Color.white;

            var button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1, 1, 1, 0.08f);
            colors.highlightedColor = new Color(Accent.r, Accent.g, Accent.b, 0.9f);
            colors.pressedColor = Accent * 0.8f;
            colors.selectedColor = new Color(1, 1, 1, 0.08f);
            colors.disabledColor = new Color(1, 1, 1, 0.03f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)textGo.transform);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = 30;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 10;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return button;
        }

        #endregion
    }
}
