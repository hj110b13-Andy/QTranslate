// QTranslate - Google Translate service (repaired 2026)
//
// The original service used https://translate.google.com/translate_a/single?client=gtx
// with a locally computed "tk" token. Google has since retired that endpoint for
// third-party clients: it now answers with an abuse-prevention "Sorry..." HTML page,
// which is why translation silently returned nothing.
//
// This version targets the Chrome-extension endpoint on clients5.google.com, which
// still serves the same data, needs no token, and additionally returns the response
// as real JSON (dj=1) instead of the old sparse-array format.
//
// Text-to-speech still works on translate.google.com, so LISTEN keeps that host.

function serviceHeader() {
    return new ServiceHeader(
        1,
        "Google",
        "Google's free online language translation service instantly translates text and web pages." +
            Const.NL2 + "https://translate.google.com" + Const.NL2 + "\u00a9 Google",
        Capability.TRANSLATE | Capability.DETECT_LANGUAGE | Capability.LISTEN);
}

var API_CLIENT = "dict-chrome-ex";
var TTS_MAX_LEN = 200;

// Translation and language detection go to the Chrome-extension endpoint;
// speech synthesis stays on the public translate.google.com host.
function serviceHost(capability, from, to) {
    return capability === Capability.LISTEN
        ? "https://translate.google.com"
        : "https://clients5.google.com";
}

// The "open in browser" link should still point at the human-facing site.
function serviceLink(text, from, to) {
    var link = "https://translate.google." + (Options.PreferredDomain || Options.GoogleDomain || "com") + "/";
    if (text) {
        from = isLanguage(from) ? codeFromLanguage(from) : "auto";
        to = isLanguage(to) ? codeFromLanguage(to) : "auto";
        link += format("#{0}/{1}/{2}", from, to, encodeGetParam(text));
    }
    return link;
}

// Indexed by QTranslate's internal language ids - do not reorder.
SupportedLanguages = [-1, "auto", "af", "az", "sq", "ar", "hy", "eu", "be", "bg", "ca", "zh-CN", "zh-TW", "hr", "cs",
    "da", "nl", "en", "et", "fi", "tl", "fr", "gl", "de", "el", "ht", "iw", "hi", "hu", "is", "id", "it", "ga", "ja",
    "ka", "ko", "lv", "lt", "mk", "ms", "mt", "no", "fa", "pl", "pt", "ro", "ru", "sr", "sk", "sl", "es", "sw", "sv",
    "th", "tr", "uk", "ur", "vi", "cy", "yi", "eo", "hmn", "la", "lo", "kk", "uz", "si", "tg", "te", "km", "mn", "kn",
    "ta", "mr", "bn", "tt"];


// ---------------------------------------------------------------------------
// Soft-wrap repair
//
// Text lifted out of a PDF - or produced by OCR on a screen region - carries a
// hard line break at the end of every visual line. Sent as-is, the translator
// treats each line as its own sentence, so "...to power the digital\ninputs."
// comes back as two unrelated fragments, and a heading gets swallowed by the
// paragraph that follows it.
//
// The reliable signal is the column width. A line that was broken by wrapping
// stops because the next word would not fit; a line that was broken on purpose
// - a heading, or the last line of a paragraph - stops with room to spare. So
// the test is simply: would the first word of the next line have fit here?
// ---------------------------------------------------------------------------
var LIST_ITEM = /^([\-\*\u2022\u00b7\u25cf\u25aa\u25e6\u2013\u2014]|\d+[.)]|\(\s*[0-9a-zA-Z]+\s*\)|[a-zA-Z][.)])\s+/;
var SENTENCE_END = /[.!?:;\u3002\uff01\uff1f\uff1a\uff1b][\"'\u201d\u2019\)\]]*$/;
var TRAILING_HYPHEN = /[\-\u2010]$/;
var LOWERCASE_HEAD = /^[a-z\u00e0-\u00ff]/;
var CJK_TAIL = /[\u2e80-\u9fff\uf900-\ufaff\uff00-\uffef]$/;
var CJK_HEAD = /^[\u2e80-\u9fff\uf900-\ufaff\uff00-\uffef]/;

// Proportional fonts mean a character count is only an approximation of the
// column, so allow a few characters of slack before calling a break deliberate.
var WIDTH_SLACK = 3;
// Below this width the text is not wrapped prose - a menu, a table column, a
// caption - and rejoining it would do more harm than good.
var MIN_COLUMN = 45;
// Above this width the text was never column-wrapped: it arrives already
// joined - QTranslate can be set to strip line breaks itself - and the breaks
// that remain are deliberate. Touching them would destroy real paragraphs.
var MAX_COLUMN = 200;
// A line this much shorter than the column, not ending a sentence, reads as a
// heading rather than as the tail of a paragraph.
var HEADING_RATIO = 0.5;

function unwrapText(text) {
    if (!text || text.indexOf("\n") < 0) {
        return text;
    }

    var raw = text.replace(/\r\n|\r/g, "\n").split("\n");
    var lines = [];
    var column = 0;
    var i;

    for (i = 0; i < raw.length; i++) {
        var trimmed = trimString(raw[i]);
        lines.push(trimmed);
        if (trimmed.length > column) {
            column = trimmed.length;
        }
    }

    if (column < MIN_COLUMN || column > MAX_COLUMN) {
        return text;
    }

    var out = [];
    // The width test has to compare against the line as it was laid out, not
    // against the paragraph accumulated so far in the output buffer.
    var previousLine = "";

    for (i = 0; i < lines.length; i++) {
        var line = lines[i];

        if (!line) {
            pushBlank(out);
            previousLine = "";
            continue;
        }

        var startsBlock = !out.length || out[out.length - 1] === "";
        if (startsBlock || keepsBreak(previousLine, line, column)) {
            out.push(line);
            previousLine = appendHeadingGap(out, line, column) ? "" : line;
            continue;
        }

        var buffer = out[out.length - 1];
        if (TRAILING_HYPHEN.test(previousLine)) {
            // "compat-" + "ibility" is one word split across lines, so the
            // hyphen goes away. "RIO-" + "47xxx" is a real hyphen in a part
            // number, so it stays.
            out[out.length - 1] = LOWERCASE_HEAD.test(line)
                ? buffer.slice(0, -1) + line
                : buffer + line;
        } else if (CJK_TAIL.test(previousLine) && CJK_HEAD.test(line)) {
            out[out.length - 1] = buffer + line;
        } else {
            out[out.length - 1] = buffer + " " + line;
        }
        previousLine = line;
    }

    return out.join("\n");
}

function pushBlank(out) {
    if (out.length && out[out.length - 1] !== "") {
        out.push("");
    }
}

// Give a heading a blank line of its own so the structure survives into the
// translation - the translator keeps the line breaks it is handed.
function appendHeadingGap(out, line, column) {
    if (SENTENCE_END.test(line) || LIST_ITEM.test(line) || line.length >= column * HEADING_RATIO) {
        return false;
    }
    out.push("");
    return true;
}

function keepsBreak(previousLine, next, column) {
    if (!previousLine) {
        return true;
    }
    if (LIST_ITEM.test(next)) {
        return true;
    }
    if (TRAILING_HYPHEN.test(previousLine)) {
        return false;
    }
    // How much room would the next line's first unit have needed? If it would
    // have fit, the break was deliberate. CJK has no word spacing, so a single
    // character is enough to show there was room left.
    var unit = CJK_HEAD.test(next) ? 1 : firstWord(next).length;
    return previousLine.length + 1 + unit <= column - WIDTH_SLACK;
}

function firstWord(text) {
    var m = text.match(/^\S+/);
    return m ? m[0] : text;
}

function makeRequest(uri, text) {
    var query = encodeUriParam(text);
    if (query.length > Const.MAX_URI_LEN) {
        return new RequestData(HttpMethod.POST, uri, "q=" + query);
    }
    return new RequestData(HttpMethod.GET, uri + "&q=" + query);
}

function serviceDetectLanguageRequest(text) {
    text = limitSource(unwrapText(prepareSource(text)));
    return makeRequest(
        "/translate_a/single?dj=1&client=" + API_CLIENT + "&sl=auto&tl=en&dt=ld&ie=UTF-8&oe=UTF-8",
        text);
}

function serviceDetectLanguageResponse(response) {
    return getSourceLanguage(parseJSON(response));
}

function serviceTranslateRequest(text, from, to) {
    text = limitSource(unwrapText(prepareSource(text)));
    var uri = format(
        "/translate_a/single?dj=1&client={0}&sl={1}&tl={2}&hl={3}&dt=t&dt=bd&dt=rm&dt=ld&ie=UTF-8&oe=UTF-8",
        API_CLIENT, codeFromLanguage(from), codeFromLanguage(to), Options.LanguageCode);
    return makeRequest(uri, text);
}

function serviceTranslateResponse(text, response, from, to) {
    var result = parseJSON(response);
    var translation = "";
    var transliteration = "";
    var i, j;

    if (result) {
        var sentences = result.sentences;
        if (sentences) {
            for (i = 0; i < sentences.length; i++) {
                var sentence = sentences[i];
                if (!sentence) {
                    continue;
                }
                if (sentence.trans) {
                    translation += sentence.trans;
                }
                if (sentence.translit) {
                    transliteration += sentence.translit;
                }
            }
        }

        // Dictionary entries, rendered the same way the original service did:
        //
        //     noun:
        //         word (synonym, synonym)
        var dict = result.dict;
        if (dict) {
            for (i = 0; i < dict.length; i++) {
                var group = dict[i];
                if (!group || !group.pos) {
                    continue;
                }
                translation += Const.NL2 + group.pos + ":";
                var entries = group.entry;
                if (entries) {
                    for (j = 0; j < entries.length; j++) {
                        var entry = entries[j];
                        if (!entry || !entry.word) {
                            continue;
                        }
                        translation += Const.NL + "    " + entry.word;
                        var back = entry.reverse_translation;
                        if (back && back.length) {
                            translation += " (" + back.join(", ") + ")";
                        }
                    }
                } else if (group.terms) {
                    for (j = 0; j < group.terms.length; j++) {
                        translation += Const.NL + "    " + group.terms[j];
                    }
                }
            }
        }

        if (!isLanguage(from)) {
            from = getSourceLanguage(result);
        }
    }

    return new ResponseData(translation, from, to, transliteration);
}

function getSourceLanguage(result) {
    if (!result) {
        return UNKNOWN_LANGUAGE;
    }
    var code = result.src;
    if (!code && result.ld_result && result.ld_result.srclangs && result.ld_result.srclangs.length) {
        code = result.ld_result.srclangs[0];
    }
    return code ? languageFromCode(code) : UNKNOWN_LANGUAGE;
}

function serviceListenRequest(text, language, slow) {
    // The tw-ob voice endpoint rejects requests longer than roughly 200 characters.
    text = limitSource(prepareSource(text), TTS_MAX_LEN);
    var uri = format("/translate_tts?ie=UTF-8&client=tw-ob&q={0}&tl={1}",
        encodeGetParam(text), codeFromLanguage(language));
    if (slow) {
        uri += "&ttsspeed=0.24";
    }
    return new RequestData(HttpMethod.GET, uri);
}
