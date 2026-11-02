function doGet() {
  return jsonResponse({ ok: true, service: "rechat-otp-mail" });
}

function doPost(event) {
  try {
    if (!event || !event.postData || !event.postData.contents) {
      return jsonResponse({ ok: false, error: "Missing request body." });
    }

    var request = JSON.parse(event.postData.contents);
    var expectedSecret = PropertiesService.getScriptProperties()
      .getProperty("EMAIL_SCRIPT_SECRET");

    if (!expectedSecret || !constantTimeEquals(String(request.secret || ""), expectedSecret)) {
      return jsonResponse({ ok: false, error: "Unauthorized." });
    }

    var recipient = String(request.to || "").trim();
    var subject = String(request.subject || "").trim();
    var htmlBody = String(request.html || "");
    if (!recipient || !subject || !htmlBody || htmlBody.length > 180000) {
      return jsonResponse({ ok: false, error: "Invalid email fields." });
    }

    if (MailApp.getRemainingDailyQuota() < 1) {
      return jsonResponse({ ok: false, error: "Daily email quota is exhausted." });
    }

    MailApp.sendEmail({
      to: recipient,
      subject: subject,
      body: "Your RE:CHAT verification code is in the HTML version of this message.",
      htmlBody: htmlBody
    });

    return jsonResponse({ ok: true });
  } catch (error) {
    console.error(error);
    return jsonResponse({ ok: false, error: "Email delivery failed." });
  }
}

function constantTimeEquals(actual, expected) {
  if (actual.length !== expected.length) return false;
  var difference = 0;
  for (var index = 0; index < actual.length; index++) {
    difference |= actual.charCodeAt(index) ^ expected.charCodeAt(index);
  }
  return difference === 0;
}

function jsonResponse(value) {
  return ContentService.createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}
