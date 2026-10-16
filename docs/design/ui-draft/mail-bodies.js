/* Sample message bodies for the sandboxed iframe (srcdoc).
   All names, brands and domains are fictional; domains use the reserved .example TLD.
   In the app, this HTML comes from the sidecar, already sanitised, with cid: images
   rewritten to data: URIs and every link given target="_blank" and title="<real URL>". */
(function () {
  "use strict";

  function svg(markup, w, h) {
    return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(
      '<svg xmlns="http://www.w3.org/2000/svg" width="' + w + '" height="' + h + '" viewBox="0 0 ' + w + " " + h + '">' + markup + "</svg>");
  }

  // Inline (cid:) images: always shown, they are part of the file.
  var LOGO = svg(
    '<rect width="190" height="30" fill="#ffffff"/>' +
    '<path d="M4 24 L14 6 L24 24 Z" fill="#2f4a3a"/><path d="M14 24 L22 12 L30 24 Z" fill="#7a9a6a"/>' +
    '<text x="38" y="21" font-family="Georgia, serif" font-size="16" fill="#2b2a27" letter-spacing="1">HALDEN &amp; MOSS</text>', 190, 30);

  // Stand-ins for the remote images, used only after "Load remote content".
  var HERO = svg(
    '<rect width="600" height="280" fill="#c9c1b2"/>' +
    '<rect y="190" width="600" height="90" fill="#8d7f68"/>' +
    '<rect x="40" y="150" width="520" height="10" fill="#5b4b37"/>' +
    '<path d="M120 70 h70 l20 30 v90 h-110 v-90 z" fill="#3e5444"/>' +
    '<path d="M265 60 h76 l22 34 v96 h-120 v-96 z" fill="#6b5a3a"/>' +
    '<path d="M420 74 h68 l18 30 v86 h-104 v-86 z" fill="#2f3b4a"/>' +
    '<circle cx="520" cy="50" r="22" fill="#efe6d4"/>', 600, 280);
  function product(fill, accent) {
    return svg('<rect width="176" height="150" fill="#eee9e0"/><path d="M48 40 h80 l10 22 v66 h-100 v-66 z" fill="' + fill + '"/>' +
      '<rect x="62" y="30" width="52" height="14" rx="7" fill="none" stroke="' + accent + '" stroke-width="5"/>', 176, 150);
  }
  var P1 = product("#3e5444", "#2a3a2f"), P2 = product("#8a6a3e", "#5e4626"), P3 = product("#2f3b4a", "#1d2631");

  function csp(remoteAllowed) {
    var img = remoteAllowed ? "data: https: http:" : "data:";
    var style = remoteAllowed ? "'unsafe-inline' https: http:" : "'unsafe-inline'";
    var font = remoteAllowed ? "data: https: http:" : "data:";
    return '<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; img-src ' + img +
      "; style-src " + style + "; font-src " + font + '">';
  }

  function doc(head, body, remoteAllowed) {
    return "<!doctype html><html><head><meta charset=\"utf-8\">" + csp(remoteAllowed) + head + "</head><body>" + body + "</body></html>";
  }

  function link(href, text, style) {
    return '<a href="' + href + '" target="_blank" title="' + href + '" style="' + (style || "") + '">' + text + "</a>";
  }

  // 1. Supplier newsletter: table layout, remote images, inline logo.
  function marketing(loaded) {
    var R = "https://img.haldenmoss.example/aw26/";
    var hero = loaded ? HERO : R + "hero-fence-rail.jpg";
    var p = loaded ? [P1, P2, P3] : [R + "p-field-tote.jpg", R + "p-roll-top.jpg", R + "p-musette.jpg"];
    var font = "font-family:Helvetica,Arial,sans-serif;";
    var cell = function (img, name, alt, price) {
      return '<td width="33%" valign="top" style="padding:0 8px;' + font + '">' +
        '<img src="' + img + '" alt="' + alt + '" width="164" height="140" style="display:block;width:100%;height:auto;background:#e6e0d6;color:#6a6358;font-size:12px">' +
        '<p style="margin:10px 0 2px;font-size:14px;color:#2b2a27;font-weight:bold">' + name + "</p>" +
        '<p style="margin:0;font-size:13px;color:#6a6358">' + price + "</p></td>";
    };
    var body =
      '<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#efebe4"><tr><td align="center" style="padding:24px 12px">' +
      '<table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff">' +
      '<tr><td style="padding:18px 28px;border-bottom:1px solid #e6e0d6"><img src="' + LOGO + '" alt="Halden &amp; Moss" width="190" height="30" style="display:block"></td>' +
      '<td align="right" style="padding:18px 28px;border-bottom:1px solid #e6e0d6;' + font + 'font-size:12px;color:#6a6358">Trade preview &middot; AW26</td></tr>' +
      '<tr><td colspan="2"><img src="' + hero + '" alt="Waxed canvas field jackets hanging on a fence rail" width="600" height="280" style="display:block;width:100%;height:auto;background:#d9d3c8;color:#6a6358;' + font + 'font-size:13px"></td></tr>' +
      '<tr><td colspan="2" style="padding:28px 28px 6px">' +
      '<h1 style="margin:0 0 12px;font-family:Georgia,serif;font-weight:normal;font-size:28px;line-height:1.2;color:#2b2a27">The field kit, ready for autumn orders</h1>' +
      '<p style="margin:0 0 12px;' + font + 'font-size:15px;line-height:1.6;color:#4a463f">Hi Mira,</p>' +
      '<p style="margin:0;' + font + 'font-size:15px;line-height:1.6;color:#4a463f">Our Autumn/Winter &rsquo;26 range is open for pre-order. The full catalogue, wholesale price list and order form are attached to this email. Orders placed by 24 October ship in week 44.</p>' +
      "</td></tr>" +
      '<tr><td colspan="2" align="center" style="padding:22px 28px 30px">' +
      link("https://trade.haldenmoss.example/aw26/order?ref=mail-1005", "Browse the range",
        "display:inline-block;padding:12px 26px;background:#2f4a3a;color:#ffffff;text-decoration:none;" + font + "font-size:14px;font-weight:bold;letter-spacing:.04em") +
      "</td></tr>" +
      '<tr><td colspan="2" style="padding:0 20px 28px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>' +
      cell(p[0], "Field tote", "Field tote in loden green", "Wholesale &pound;38 &middot; RRP &pound;85") +
      cell(p[1], "Roll-top pack", "Roll-top pack in tobacco", "Wholesale &pound;64 &middot; RRP &pound;145") +
      cell(p[2], "Musette", "Musette in navy", "Wholesale &pound;29 &middot; RRP &pound;65") +
      "</tr></table></td></tr>" +
      '<tr><td colspan="2" style="padding:20px 28px;background:#f6f3ee;' + font + 'font-size:12px;line-height:1.6;color:#6a6358">' +
      "Halden &amp; Moss Ltd &middot; 14 Mill Yard &middot; Kendal<br>You receive trade updates because Okafor Outfitters holds a wholesale account. " +
      link("https://trade.haldenmoss.example/preferences?u=8f2c", "Manage email preferences", "color:#6a6358") +
      "</td></tr></table></td></tr></table>";
    return doc('<style>body{margin:0}</style>', body, loaded);
  }

  // 2. Chinese letter (GBK in the source file; decoded by the sidecar).
  function chinese() {
    var f = "font-family:'PingFang SC','Microsoft YaHei','Noto Sans CJK SC',sans-serif;";
    var td = 'style="border:1px solid #d6d6d6;padding:6px 10px;' + f + 'font-size:14px"';
    var body =
      '<div style="' + f + 'font-size:14px;line-height:1.8;color:#222;padding:24px 28px;max-width:720px">' +
      "<p>各位供应商伙伴：</p>" +
      "<p>您好！为做好 2026 年第四季度的对账与结算工作，现将有关安排通知如下，请各位于规定时间内完成核对并回复确认。</p>" +
      '<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;margin:12px 0">' +
      '<tr style="background:#f3f5f7"><th ' + td + '>事项</th><th ' + td + '>截止时间</th><th ' + td + '>负责人</th></tr>' +
      "<tr><td " + td + ">核对对账单（见附件）</td><td " + td + ">10 月 24 日</td><td " + td + ">各供应商</td></tr>" +
      "<tr><td " + td + ">回传开票信息确认函</td><td " + td + ">10 月 28 日</td><td " + td + ">各供应商</td></tr>" +
      "<tr><td " + td + ">开具增值税专用发票</td><td " + td + ">11 月 5 日</td><td " + td + ">各供应商</td></tr>" +
      "<tr><td " + td + ">付款安排</td><td " + td + ">11 月 20 日前</td><td " + td + ">兰溪贸易财务部</td></tr>" +
      "</table>" +
      "<p>如对账单金额与贵司记录不一致，请在回复中列明差异明细，我们会在两个工作日内与您联系。</p>" +
      "<p>感谢各位一直以来的支持与配合！</p>" +
      '<p style="margin-top:24px;color:#555">陈思远<br>采购部 · 兰溪贸易有限公司<br>电话：0571-0000 0000</p>' +
      "</div>";
    return doc("<style>body{margin:0}</style>", body, false);
  }

  // 3. Plain text body. Wrapped by the sidecar; line breaks preserved.
  function plain(text) {
    var esc = text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    return doc('<style>body{margin:0}pre{margin:0;padding:22px 28px;white-space:pre-wrap;overflow-wrap:anywhere;' +
      'font:14px/1.6 system-ui,-apple-system,"Segoe UI",Ubuntu,"PingFang SC","Microsoft YaHei UI",sans-serif;color:#1f2421}</style>',
      "<pre>" + esc + "</pre>", false);
  }

  // 4. Nested reply (Outlook-style HTML).
  function nested() {
    var f = "font-family:Calibri,Carlito,Arial,sans-serif;font-size:15px;line-height:1.5;color:#1f1f1f;";
    var body =
      '<div style="' + f + 'padding:22px 28px;max-width:720px">' +
      "<p>Hi Mira,</p><p>Confirmed: both pallets leave our Kendal warehouse on Tuesday 20 October and should reach you on the 22nd. The schedule is attached, and the carrier's confirmation is attached as a separate email.</p>" +
      "<p>If the 22nd doesn't work, let me know by Friday and I'll move it.</p>" +
      "<p>Thanks,<br>Ines</p>" +
      '<div style="border-top:1px solid #e1e1e1;margin-top:18px;padding-top:10px;color:#555;font-size:13px">' +
      "<b>From:</b> Mira Okafor &lt;mira@okafor-outfitters.example&gt;<br><b>Sent:</b> Monday, 28 September 2026 17:02<br><b>Subject:</b> Pallet delivery dates</div>" +
      '<p style="color:#555">Could you confirm when the two AW26 pallets will arrive? We need a weekday delivery; the loading bay is closed at weekends.</p>' +
      "</div>";
    return doc("<style>body{margin:0}</style>", body, false);
  }

  // 5. A notice whose link text and real address disagree.
  function bank() {
    var f = "font-family:Helvetica,Arial,sans-serif;";
    var body =
      '<div style="' + f + 'padding:24px 28px;max-width:640px;color:#1f2a36;font-size:15px;line-height:1.6">' +
      '<div style="font-size:20px;font-weight:bold;color:#1d3b6a;letter-spacing:.02em;margin-bottom:16px">Ferncliff Bank</div>' +
      "<p style=\"margin:0 0 12px\">Dear customer,</p>" +
      "<p style=\"margin:0 0 12px\">We could not verify your account details during a routine review. To keep your account active, confirm your details before 20 October at:</p>" +
      '<p style="margin:0 0 16px">' + link("https://ferncliff-verify.example.net/login?session=8841&amp;r=acct", "https://ferncliff-bank.example/secure", "color:#1d5fbf;font-weight:bold") + "</p>" +
      '<p style="margin:0;color:#5a6672;font-size:13px">Ferncliff Bank will never ask for your password by email.</p>' +
      "</div>";
    return doc("<style>body{margin:0}</style>", body, false);
  }

  window.MAIL_BODIES = { marketing: marketing, chinese: chinese, plain: plain, nested: nested, bank: bank };
})();
