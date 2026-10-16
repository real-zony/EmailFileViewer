#!/usr/bin/env node
/* WCAG 2.x contrast check for every palette in both modes.
   Usage:
     node check-contrast.js            full report, exit code 1 if anything fails
     node check-contrast.js --md       summary table (Markdown) for README
     node check-contrast.js --tokens   token tables (Markdown) for README
   Thresholds: "text" pairs need 4.5:1; "ui" pairs (icons, focus rings, large text) need 3:1. */
"use strict";
var PALETTES = require("./palettes.js").PALETTES;

// [foreground, background, level, where it is used]
var PAIRS = [
  ["ink", "desk", "text", "body text on window ground"],
  ["ink", "surface", "text", "text on panels"],
  ["ink", "raised", "text", "text on cards and dialogs"],
  ["ink", "sunken", "text", "active text near the tab strip"],
  ["ink", "accent-soft", "text", "selected option label"],
  ["ink-2", "desk", "text", "secondary text, header labels"],
  ["ink-2", "surface", "text", "body label, field hints"],
  ["ink-2", "raised", "text", "sizes, captions on cards"],
  ["ink-2", "sunken", "text", "inactive tab labels"],
  ["accent", "desk", "text", "link buttons (+N more, Show all)"],
  ["accent", "surface", "text", "links on panels"],
  ["accent", "raised", "text", "links in dialogs"],
  ["accent-ink", "accent", "text", "primary button label"],
  ["accent-ink", "accent-hover", "text", "primary button label, hover"],
  ["accent-soft-ink", "accent-soft", "text", "drop state title, message badge"],
  ["warn-ink", "warn-bg", "text", "remote content notice"],
  ["warn-ink", "raised", "text", "warn button, Save only flag"],
  ["danger-ink", "danger-bg", "text", "error text on tint"],
  ["danger-ink", "desk", "text", "error reason label"],
  ["ink-3", "desk", "ui", "icons on window ground"],
  ["ink-3", "surface", "ui", "icons on panels"],
  ["ink-3", "raised", "ui", "icons on cards"],
  ["ink-3", "sunken", "ui", "tab icons"],
  ["focus", "desk", "ui", "focus ring"],
  ["focus", "surface", "ui", "focus ring on panels"],
  ["focus", "raised", "ui", "focus ring on cards"],
  ["focus", "sunken", "ui", "focus ring in tab strip"],
  ["accent", "accent-soft", "ui", "selected option border"],
  ["warn-icon", "warn-bg", "ui", "notice icon"],
  ["danger-icon", "danger-bg", "ui", "error icon"],
  ["danger-icon", "raised", "ui", "toast icon and edge"]
];
var MIN = { text: 4.5, ui: 3 };

function lum(hex) {
  var h = hex.replace("#", "");
  var c = [0, 2, 4].map(function (i) {
    var v = parseInt(h.slice(i, i + 2), 16) / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}
function ratio(a, b) {
  var la = lum(a), lb = lum(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

var results = [];
PALETTES.forEach(function (p) {
  ["light", "dark"].forEach(function (mode) {
    var t = p[mode];
    PAIRS.forEach(function (pair) {
      var fg = t[pair[0]], bg = t[pair[1]];
      if (!fg || !bg) throw new Error(p.id + "/" + mode + ": missing token " + (!fg ? pair[0] : pair[1]));
      var r = ratio(fg, bg);
      results.push({ palette: p, mode: mode, fg: pair[0], bg: pair[1], level: pair[2], use: pair[3], ratio: r, ok: r >= MIN[pair[2]] });
    });
  });
});
var failures = results.filter(function (r) { return !r.ok; });
var args = process.argv.slice(2);

function f(n) { return n.toFixed(1); }

if (args.indexOf("--md") >= 0) {
  console.log("| Palette | Mode | Lowest text pair (≥ 4.5) | Lowest icon / focus pair (≥ 3) | Result |");
  console.log("|---|---|---|---|---|");
  PALETTES.forEach(function (p) {
    ["light", "dark"].forEach(function (mode) {
      var rs = results.filter(function (r) { return r.palette === p && r.mode === mode; });
      function low(level) {
        var x = rs.filter(function (r) { return r.level === level; }).sort(function (a, b) { return a.ratio - b.ratio; })[0];
        return f(x.ratio) + " (`" + x.fg + "` on `" + x.bg + "`)";
      }
      var ok = rs.every(function (r) { return r.ok; });
      console.log("| " + p.name.en + " " + p.name.zh + " | " + mode + " | " + low("text") + " | " + low("ui") + " | " + (ok ? "pass" : "**FAIL**") + " |");
    });
  });
} else if (args.indexOf("--tokens") >= 0) {
  PALETTES.forEach(function (p) {
    console.log("\n#### " + p.name.en + " / " + p.name.zh + " (`" + p.id + "`)\n");
    console.log(p.blurb.zh + "\n");
    console.log("| Token | Light | Dark |\n|---|---|---|");
    Object.keys(p.light).forEach(function (k) {
      console.log("| `--efv-" + k + "` | `" + p.light[k] + "` | `" + p.dark[k] + "` |");
    });
  });
} else {
  PALETTES.forEach(function (p) {
    ["light", "dark"].forEach(function (mode) {
      console.log("\n" + p.id + " / " + mode);
      results.filter(function (r) { return r.palette === p && r.mode === mode; }).forEach(function (r) {
        console.log("  " + (r.ok ? "ok  " : "FAIL") + "  " + f(r.ratio).padStart(5) + ":1  " + (r.level === "text" ? "text 4.5" : "ui   3.0") + "  " +
          (r.fg + " on " + r.bg).padEnd(30) + r.use);
      });
    });
  });
  console.log("\n" + results.length + " pairs checked, " + failures.length + " failing.");
}
process.exitCode = failures.length ? 1 : 0;
