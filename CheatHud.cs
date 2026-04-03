using UnityEngine;

namespace CasinoCheat
{
    /// <summary>
    /// MonoBehaviour що рендерить підказку через IMGUI (OnGUI).
    /// Один екземпляр створюється при завантаженні плагіну.
    /// </summary>
    internal class CheatHud : MonoBehaviour
    {
        private static CheatHud? _instance;

        private string _message = "";
        private Color _color = Color.yellow;
        private float _timeLeft = 0f;

        private GUIStyle? _style;

        private void Awake()
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Викликається з патчів щоб показати повідомлення.</summary>
        public static void ShowMessage(string message, Color color, float duration)
        {
            if (_instance == null)
            {
                var go = new GameObject("CasinoCheatHUD");
                _instance = go.AddComponent<CheatHud>();
            }

            _instance._message = message;
            _instance._color = color;
            _instance._timeLeft = duration;
        }

        private void Update()
        {
            if (_timeLeft > 0f)
                _timeLeft -= Time.deltaTime;
        }

        public static void Hide()
        {
            if (_instance != null)
                _instance._timeLeft = 0f;
        }

        private void OnGUI()
        {
            if (_timeLeft <= 0f || string.IsNullOrEmpty(_message))
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 22,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                _style.normal.textColor = Color.white;
            }

            // Fade out в останню секунду
            float alpha = Mathf.Clamp01(_timeLeft);
            Color boxColor = new Color(_color.r * 0.15f, _color.g * 0.15f, _color.b * 0.15f, alpha * 0.85f);
            _style.normal.textColor = new Color(_color.r, _color.g, _color.b, alpha);

            float w = 420f;
            float h = 90f;
            float x = (Screen.width - w) / 2f;
            float y = Screen.height * 0.12f;

            GUI.backgroundColor = boxColor;
            GUI.Box(new Rect(x, y, w, h), _message, _style);
            GUI.backgroundColor = Color.white;
        }
    }
}
