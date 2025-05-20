using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace InstantPipes
{
    public static class PipePointIcon
    {
        // Dictionary to cache textures by color to avoid recreating them constantly
        private static Dictionary<Color, Texture2D> _startPointIcons = new Dictionary<Color, Texture2D>();
        private static Dictionary<Color, Texture2D> _endPointIcons = new Dictionary<Color, Texture2D>();
        
        private static Texture2D CreateStartPointTexture(Color fillColor)
        {
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[32 * 32];
            
            // Make sure color is fully opaque
            fillColor.a = 1.0f;
            
            // Create a darker version of the fill color for the outline
            Color outlineColor = new Color(
                Mathf.Clamp01(fillColor.r * 0.6f),
                Mathf.Clamp01(fillColor.g * 0.6f),
                Mathf.Clamp01(fillColor.b * 0.6f),
                1.0f
            );
            
            // Create a brighter version of the fill color for inner part
            Color innerColor = new Color(
                Mathf.Clamp01(fillColor.r * 1.5f),
                Mathf.Clamp01(fillColor.g * 1.5f),
                Mathf.Clamp01(fillColor.b * 1.5f),
                1.0f
            );
            
            // Star shape for start point (같은 모양 사용)
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // Convert to center coordinates
                    float cx = x - 16;
                    float cy = 16 - y;
                    
                    // Convert to polar coordinates
                    float r = Mathf.Sqrt(cx * cx + cy * cy);
                    float angle = Mathf.Atan2(cy, cx);
                    
                    // Create a star shape with 5 points
                    float wave = Mathf.Abs(Mathf.Cos(angle * 5 / 2));
                    
                    if (r < 6 + wave * 10)
                    {
                        // Center area
                        if (r < 6)
                        {
                            pixels[y * 32 + x] = innerColor;
                        }
                        // Star shape
                        else if (r < 6 + wave * 10)
                        {
                            pixels[y * 32 + x] = fillColor;
                        }
                    }
                    else if (r < 7 + wave * 10)
                    {
                        // Outline
                        pixels[y * 32 + x] = outlineColor;
                    }
                    else
                    {
                        // Transparent background
                        pixels[y * 32 + x] = new Color(0, 0, 0, 0);
                    }
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        private static Texture2D CreateEndPointTexture(Color fillColor)
        {
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[32 * 32];
            
            // Make sure color is fully opaque
            fillColor.a = 1.0f;
            
            // Create a darker version of the fill color for the outline
            Color outlineColor = new Color(
                Mathf.Clamp01(fillColor.r * 0.6f),
                Mathf.Clamp01(fillColor.g * 0.6f),
                Mathf.Clamp01(fillColor.b * 0.6f),
                1.0f
            );
            
            // Create a brighter version of the fill color for inner part
            Color innerColor = new Color(
                Mathf.Clamp01(fillColor.r * 1.5f),
                Mathf.Clamp01(fillColor.g * 1.5f),
                Mathf.Clamp01(fillColor.b * 1.5f),
                1.0f
            );
            
            // Flag shape for end point (시작점과 다른 모양 사용)
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 16;
                    float dy = y - 16;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    // Square with a horizontal rectanglar flag
                    if (x >= 11 && x <= 15 && y >= 8 && y <= 24)
                    {
                        // Vertical pole
                        pixels[y * 32 + x] = outlineColor;
                    }
                    else if (x > 15 && x <= 26 && y >= 8 && y <= 16)
                    {
                        // Flag
                        if (x == 26 || y == 8 || y == 16)
                        {
                            pixels[y * 32 + x] = outlineColor; // Outline
                        }
                        else
                        {
                            pixels[y * 32 + x] = fillColor; // Fill
                        }
                    }
                    else if (x >= 10 && x <= 16 && y >= 24 && y <= 26)
                    {
                        // Base of pole
                        pixels[y * 32 + x] = outlineColor;
                    }
                    else
                    {
                        // Transparent background
                        pixels[y * 32 + x] = new Color(0, 0, 0, 0);
                    }
                }
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        public static void DrawStartPointIcon(Vector3 position, float size = 1.0f, Color? color = null)
        {
            Color iconColor = color ?? Color.white;
            
            // Get or create texture with this color
            if (!_startPointIcons.TryGetValue(iconColor, out Texture2D texture))
            {
                texture = CreateStartPointTexture(iconColor);
                _startPointIcons[iconColor] = texture;
            }
            
            Handles.BeginGUI();
            Vector3 screenPos = HandleUtility.WorldToGUIPoint(position);
            Rect rect = new Rect(screenPos.x - 16 * size, screenPos.y - 16 * size, 32 * size, 32 * size);
            GUI.DrawTexture(rect, texture);
            Handles.EndGUI();
        }
        
        public static void DrawEndPointIcon(Vector3 position, float size = 1.0f, Color? color = null)
        {
            Color iconColor = color ?? Color.white;
            
            // Get or create texture with this color
            if (!_endPointIcons.TryGetValue(iconColor, out Texture2D texture))
            {
                texture = CreateEndPointTexture(iconColor);
                _endPointIcons[iconColor] = texture;
            }
            
            Handles.BeginGUI();
            Vector3 screenPos = HandleUtility.WorldToGUIPoint(position);
            Rect rect = new Rect(screenPos.x - 16 * size, screenPos.y - 16 * size, 32 * size, 32 * size);
            GUI.DrawTexture(rect, texture);
            Handles.EndGUI();
        }
    }
} 