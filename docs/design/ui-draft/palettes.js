/* Colour palettes: the single source of truth for theme colours.
   - build-themes.js turns this into themes.css
   - check-contrast.js checks every palette in both modes
   - the prototype reads names and blurbs for the settings dialog
   Keys are CSS custom property names without the "--efv-" prefix.
   Works both in Node (module.exports) and in the browser (window.EFV_PALETTES). */
(function (root) {
  "use strict";

  // Semantic colours keep the same meaning (amber = decide, red = failed) in every palette.
  var SEMANTIC = {
    light: {
      "warn-bg": "#fbf1dc", "warn-line": "#e5c584", "warn-ink": "#6b4700", "warn-icon": "#9a6500",
      "danger-bg": "#fbe8e5", "danger-line": "#eab5ad", "danger-ink": "#8c2217", "danger-icon": "#bd3526"
    },
    dark: {
      "warn-bg": "#2b2412", "warn-line": "#5a4719", "warn-ink": "#f2cb7a", "warn-icon": "#e9b44c",
      "danger-bg": "#31191a", "danger-line": "#6a2d28", "danger-ink": "#ffaa9d", "danger-icon": "#ff7c6c"
    }
  };

  var PALETTES = [
    {
      id: "verdigris",
      name: { en: "Verdigris", zh: "铜绿" },
      blurb: { en: "Cool mineral greys with a green-blue accent. The default.", zh: "偏冷的矿物灰，青绿色强调色。默认方案。" },
      light: {
        desk: "#e6eae8", surface: "#f1f4f2", raised: "#ffffff", sunken: "#d8dfdb",
        ink: "#16201c", "ink-2": "#4a5752", "ink-3": "#67736e",
        line: "#cbd3cf", "line-strong": "#a9b5b0",
        accent: "#17695b", "accent-hover": "#10574b", "accent-ink": "#ffffff",
        "accent-soft": "#dcede8", "accent-soft-ink": "#0f4f44", focus: "#17695b"
      },
      dark: {
        desk: "#121715", surface: "#1a201e", raised: "#222927", sunken: "#0c100f",
        ink: "#e3e9e6", "ink-2": "#a6b2ad", "ink-3": "#85918c",
        line: "#2f3835", "line-strong": "#46524e",
        accent: "#5ec2ae", "accent-hover": "#7ad3c1", "accent-ink": "#072019",
        "accent-soft": "#173832", "accent-soft-ink": "#9fe0d2", focus: "#7ad8c5"
      }
    },
    {
      id: "graphite",
      name: { en: "Graphite", zh: "石墨" },
      blurb: { en: "Neutral greys and no colour accent. Nothing competes with the message.", zh: "纯中性灰，不用彩色强调，界面完全不和邮件抢颜色。" },
      light: {
        desk: "#e8e8e7", surface: "#f3f3f2", raised: "#ffffff", sunken: "#dadad8",
        ink: "#1b1b1b", "ink-2": "#4f4f4e", "ink-3": "#6b6b6a",
        line: "#cfcfcd", "line-strong": "#ababa9",
        accent: "#2d2d2d", "accent-hover": "#121212", "accent-ink": "#ffffff",
        "accent-soft": "#e1e1e0", "accent-soft-ink": "#1b1b1b", focus: "#1b1b1b"
      },
      dark: {
        desk: "#161616", surface: "#1e1e1e", raised: "#272727", sunken: "#0f0f0f",
        ink: "#e8e8e8", "ink-2": "#ababab", "ink-3": "#8a8a8a",
        line: "#333333", "line-strong": "#4b4b4b",
        accent: "#e4e4e4", "accent-hover": "#ffffff", "accent-ink": "#141414",
        "accent-soft": "#363636", "accent-soft-ink": "#f2f2f2", focus: "#f2f2f2"
      }
    },
    {
      id: "indigo",
      name: { en: "Indigo", zh: "靛蓝" },
      blurb: { en: "Blue-grey surfaces with an ink-blue accent. Familiar from most mail apps.", zh: "蓝灰底色，墨蓝强调色，接近多数邮件客户端的习惯。" },
      light: {
        desk: "#e7e9ef", surface: "#f2f3f7", raised: "#ffffff", sunken: "#d9dce6",
        ink: "#161a2b", "ink-2": "#4a5068", "ink-3": "#676d84",
        line: "#cdd1dc", "line-strong": "#a9afc0",
        accent: "#3949a0", "accent-hover": "#2c3a86", "accent-ink": "#ffffff",
        "accent-soft": "#e0e4f6", "accent-soft-ink": "#26337a", focus: "#3949a0"
      },
      dark: {
        desk: "#121420", surface: "#191c2a", raised: "#212436", sunken: "#0b0d16",
        ink: "#e3e5f0", "ink-2": "#a7abc2", "ink-3": "#868ba3",
        line: "#2d3146", "line-strong": "#444a64",
        accent: "#8f9cf0", "accent-hover": "#a9b3f5", "accent-ink": "#10142e",
        "accent-soft": "#222a52", "accent-soft-ink": "#c3cbfa", focus: "#a9b3f5"
      }
    },
    {
      id: "plum",
      name: { en: "Plum", zh: "梅紫" },
      blurb: { en: "Warm stone greys with a muted plum accent. Softer, still sober.", zh: "偏暖的石灰色，低饱和的梅紫强调色。更柔和，但依然克制。" },
      light: {
        desk: "#ebe7e4", surface: "#f5f2f0", raised: "#ffffff", sunken: "#ddd7d3",
        ink: "#221b1f", "ink-2": "#574c52", "ink-3": "#74686e",
        line: "#d4ccc8", "line-strong": "#b3a9a5",
        accent: "#6e3f78", "accent-hover": "#5a3063", "accent-ink": "#ffffff",
        "accent-soft": "#efe3f1", "accent-soft-ink": "#552c5e", focus: "#6e3f78"
      },
      dark: {
        desk: "#18141a", surface: "#201b22", raised: "#29232b", sunken: "#100d11",
        ink: "#ece4ea", "ink-2": "#b5a9b2", "ink-3": "#958890",
        line: "#352d37", "line-strong": "#4d434f",
        accent: "#d3a3dc", "accent-hover": "#e2bde9", "accent-ink": "#2a1230",
        "accent-soft": "#3a2840", "accent-soft-ink": "#ecd2f1", focus: "#e2bde9"
      }
    },
    {
      id: "contrast",
      name: { en: "High contrast", zh: "高对比度" },
      blurb: { en: "Black, white and strong borders. Every text pair is 7:1 or more.", zh: "黑白配色，边框更重，所有文字对比度不低于 7:1。" },
      light: {
        desk: "#ffffff", surface: "#f2f2f2", raised: "#ffffff", sunken: "#e4e4e4",
        ink: "#000000", "ink-2": "#262626", "ink-3": "#4d4d4d",
        line: "#767676", "line-strong": "#3d3d3d",
        accent: "#0a4a9e", "accent-hover": "#073a7c", "accent-ink": "#ffffff",
        "accent-soft": "#dce8fa", "accent-soft-ink": "#052e63", focus: "#000000",
        "warn-bg": "#fff3d1", "warn-line": "#7a5200", "warn-ink": "#3d2800", "warn-icon": "#6b4700",
        "danger-bg": "#ffe8e5", "danger-line": "#9b1d10", "danger-ink": "#6b0c03", "danger-icon": "#9b1d10"
      },
      dark: {
        desk: "#000000", surface: "#0e0e0e", raised: "#141414", sunken: "#1c1c1c",
        ink: "#ffffff", "ink-2": "#dcdcdc", "ink-3": "#b3b3b3",
        line: "#8c8c8c", "line-strong": "#cccccc",
        accent: "#8dbcff", "accent-hover": "#b3d2ff", "accent-ink": "#000000",
        "accent-soft": "#0c2a55", "accent-soft-ink": "#d6e6ff", focus: "#ffffff",
        "warn-bg": "#231a00", "warn-line": "#c99a2e", "warn-ink": "#ffdc8a", "warn-icon": "#ffc94a",
        "danger-bg": "#2a0b08", "danger-line": "#ff8a7a", "danger-ink": "#ffbdb3", "danger-icon": "#ff8a7a"
      }
    }
  ];

  PALETTES.forEach(function (p) {
    ["light", "dark"].forEach(function (m) {
      // Palette values first; shared semantic values fill in whatever the palette does not override.
      Object.keys(SEMANTIC[m]).forEach(function (k) { if (!(k in p[m])) p[m][k] = SEMANTIC[m][k]; });
    });
  });

  var api = { PALETTES: PALETTES, DEFAULT_PALETTE: "verdigris", DEFAULT_MODE: "system" };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.EFV_PALETTES = api;
})(this);
