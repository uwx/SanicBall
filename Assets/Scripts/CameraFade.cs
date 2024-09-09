using System;
using UnityEngine;

namespace Sanicball
{
    public class CameraFade : MonoBehaviour
    {
        public GUIStyle m_BackgroundStyle = new GUIStyle();

        private float m_FadeFinishTime = 0;
        
        // Style for background tiling
        public Texture2D m_FadeTexture;

        // 1x1 pixel texture used for fading
        public Color m_CurrentScreenOverlayColor = new Color(0, 0, 0, 0);

        // default starting color: black and fully transparrent
        public Color m_TargetScreenOverlayColor = new Color(0, 0, 0, 0);

        // default target color: black and fully transparrent
        public Color m_DeltaColor = new Color(0, 0, 0, 0);

        // the delta-color is basically the "speed / second" at which the current color should change
        public int m_FadeGUIDepth = -1000;

        public float m_FadeDelay = 0;

        // make sure this texture is drawn on top of everything
        public Action m_OnFadeFinish = null;

        private static CameraFade mInstance = null;

        private static bool m_Loading;
        private static Color m_TextColor;
        private static bool m_FadeTextIn = true;
        private static float m_TextAlpha = 0;

        private static CameraFade instance
        {
            get
            {
                if (mInstance == null)
                {
                    mInstance = GameObject.FindObjectOfType(typeof(CameraFade)) as CameraFade;

                    if (mInstance == null)
                    {
                        mInstance = new GameObject("CameraFade").AddComponent<CameraFade>();
                    }
                }

                return mInstance;
            }
        }

        /// <summary>
        /// Starts the fade from color newScreenOverlayColor. If isFadeIn, start fully opaque, else start transparent.
        /// </summary>
        /// <param name='newScreenOverlayColor'>
        /// Target screen overlay Color.
        /// </param>
        /// <param name='fadeDuration'>
        /// Fade duration.
        /// </param>
        public static void StartAlphaFade(Color newScreenOverlayColor, bool isFadeIn, float fadeDuration, bool loading = false)
        {
            m_Loading = loading;

            instance.m_OnFadeFinish = null;

            if (fadeDuration <= 0.0f)
            {
                SetScreenOverlayColor(newScreenOverlayColor);
            }
            else
            {
                if (isFadeIn)
                {
                    instance.m_TargetScreenOverlayColor = new Color(newScreenOverlayColor.r, newScreenOverlayColor.g, newScreenOverlayColor.b, 0);
                    SetScreenOverlayColor(newScreenOverlayColor);
                }
                else
                {
                    instance.m_TargetScreenOverlayColor = newScreenOverlayColor;
                    SetScreenOverlayColor(new Color(newScreenOverlayColor.r, newScreenOverlayColor.g, newScreenOverlayColor.b, 0));
                }

                instance.m_FadeFinishTime = fadeDuration + Time.time;
                instance.m_DeltaColor = (instance.m_TargetScreenOverlayColor - instance.m_CurrentScreenOverlayColor) / fadeDuration;
            }
        }

        /// <summary>
        /// Starts the fade from color newScreenOverlayColor. If isFadeIn, start fully opaque, else start transparent, after a delay.
        /// </summary>
        /// <param name='newScreenOverlayColor'>
        /// New screen overlay color.
        /// </param>
        /// <param name='fadeDuration'>
        /// Fade duration.
        /// </param>
        /// <param name='fadeDelay'>
        /// Fade delay.
        /// </param>
        public static void StartAlphaFade(Color newScreenOverlayColor, bool isFadeIn, float fadeDuration, float fadeDelay)
        {
            if (fadeDuration <= 0.0f)
            {
                SetScreenOverlayColor(newScreenOverlayColor);
            }
            else
            {
                instance.m_FadeDelay = Time.time + fadeDelay;
                instance.m_FadeFinishTime = fadeDuration + Time.time;

                if (isFadeIn)
                {
                    instance.m_TargetScreenOverlayColor = new Color(newScreenOverlayColor.r, newScreenOverlayColor.g, newScreenOverlayColor.b, 0);
                    SetScreenOverlayColor(newScreenOverlayColor);
                }
                else
                {
                    instance.m_TargetScreenOverlayColor = newScreenOverlayColor;
                    SetScreenOverlayColor(new Color(newScreenOverlayColor.r, newScreenOverlayColor.g, newScreenOverlayColor.b, 0));
                }

                instance.m_DeltaColor = (instance.m_TargetScreenOverlayColor - instance.m_CurrentScreenOverlayColor) / fadeDuration;
            }
        }

        /// <summary>
        /// Starts the fade from color newScreenOverlayColor. If isFadeIn, start fully opaque, else start transparent, after a delay, with Action OnFadeFinish.
        /// </summary>
        /// <param name='newScreenOverlayColor'>
        /// New screen overlay color.
        /// </param>
        /// <param name='fadeDuration'>
        /// Fade duration.
        /// </param>
        /// <param name='fadeDelay'>
        /// Fade delay.
        /// </param>
        /// <param name='OnFadeFinish'>
        /// On fade finish, doWork().
        /// </param>
        public static void StartAlphaFade(Color newScreenOverlayColor, bool isFadeIn, float fadeDuration, float fadeDelay, Action OnFadeFinish, bool loading = false)
        {
            m_Loading = loading;

            if (fadeDuration <= 0.0f)
            {
                SetScreenOverlayColor(newScreenOverlayColor);
            }
            else
            {
                instance.m_OnFadeFinish = OnFadeFinish;
                instance.m_FadeDelay = Time.time + fadeDelay;
                instance.m_FadeFinishTime = fadeDuration + Time.time;

                if (isFadeIn)
                {
                    instance.m_TargetScreenOverlayColor = new Color(newScreenOverlayColor.r, newScreenOverlayColor.g, newScreenOverlayColor.b, 0);
                    SetScreenOverlayColor(newScreenOverlayColor);
                }
                else
                {
                    instance.m_TargetScreenOverlayColor = newScreenOverlayColor;
                    SetScreenOverlayColor(new Color(newScreenOverlayColor.r, newScreenOverlayColor.g, newScreenOverlayColor.b, 0));
                }
                instance.m_DeltaColor = (instance.m_TargetScreenOverlayColor - instance.m_CurrentScreenOverlayColor) / fadeDuration;
            }
        }

        // Initialize the texture, background-style and initial color:
        public void init()
        {
            instance.m_FadeTexture = new Texture2D(1, 1);
            instance.m_BackgroundStyle.normal.background = instance.m_FadeTexture;
        }

        /// <summary>
        /// Sets the color of the screen overlay instantly.  Useful to start a fade.
        /// </summary>
        /// <param name='newScreenOverlayColor'>
        /// New screen overlay color.
        /// </param>
        private static void SetScreenOverlayColor(Color newScreenOverlayColor)
        {
            instance.m_CurrentScreenOverlayColor = newScreenOverlayColor;
            instance.m_FadeTexture.SetPixel(0, 0, instance.m_CurrentScreenOverlayColor);
            instance.m_FadeTexture.Apply();
        }

        private void Awake()
        {
            if (mInstance == null)
            {
                mInstance = this as CameraFade;
                instance.init();
            }

            m_TextColor = Color.white;
        }

        // Draw the texture and perform the fade:
        private void OnGUI()
        {
            // If delay is over...
            if (Time.time >= instance.m_FadeDelay)
            {
                // If the current color of the screen is not equal to the desired color: keep fading!
                if (instance.m_CurrentScreenOverlayColor != instance.m_TargetScreenOverlayColor)
                {
                    // If the difference between the current alpha and the desired alpha is smaller than delta-alpha * deltaTime, then we're pretty much done fading:
                    if ((Mathf.Abs(instance.m_CurrentScreenOverlayColor.a - instance.m_TargetScreenOverlayColor.a) < Mathf.Abs(instance.m_DeltaColor.a) * Time.deltaTime)
                        || Time.time > instance.m_FadeFinishTime)
                    {
                        instance.m_CurrentScreenOverlayColor = instance.m_TargetScreenOverlayColor;
                        SetScreenOverlayColor(instance.m_CurrentScreenOverlayColor);
                        instance.m_DeltaColor = new Color(0, 0, 0, 0);

                        instance.m_OnFadeFinish?.Invoke();
                        instance.m_OnFadeFinish = null;

                        // Die();
                    }
                    else
                    {
                        // Fade!
                        SetScreenOverlayColor(instance.m_CurrentScreenOverlayColor + instance.m_DeltaColor * Time.deltaTime);
                    }
                }
            }
            // Only draw the texture when the alpha value is greater than 0:
            if (m_CurrentScreenOverlayColor.a > 0)
            {
                GUI.depth = instance.m_FadeGUIDepth;
                GUI.Label(new Rect(-10, -10, Screen.width + 10, Screen.height + 10), instance.m_FadeTexture, instance.m_BackgroundStyle);
            }

            if (m_Loading || m_TextAlpha > 0)
            {
                GUI.depth = instance.m_FadeGUIDepth + 1;
                var content = new GUIContent("Now Loading");
                var style = new GUIStyle { fontSize = 36 };

                if (m_FadeTextIn)
                {
                    m_TextAlpha = Mathf.Min(m_TextAlpha + (1f * Time.unscaledDeltaTime), 1);
                    if (m_TextAlpha == 1)
                        m_FadeTextIn = false;
                }
                else
                {
                    m_TextAlpha = Mathf.Max(m_TextAlpha - (1f * Time.unscaledDeltaTime), 0);
                    if (m_TextAlpha == 0)
                        m_FadeTextIn = true;
                }

                m_TextColor.a = m_TextAlpha;

                style.normal.textColor = m_TextColor;
                var size = style.CalcScreenSize(style.CalcSize(content));

                GUI.Label(new Rect(new Vector2(Screen.width - (size.x + 32), Screen.height - (size.y + 32)), size), content, style);
            }
        }

        private void Die()
        {
            mInstance = null;
            Destroy(gameObject);
        }

        private void OnApplicationQuit()
        {
            mInstance = null;
        }
    }
}