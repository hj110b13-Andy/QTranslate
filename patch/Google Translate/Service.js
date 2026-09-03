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
// The parenthesised-label branch only matches short, lowercase-or-numeric
// labels ("(a)", "(iv)", "(12)") - the kind an outline actually uses. A
// longer or mixed-case run like "(mAP)" or "(RGB)" is an acronym that
// happens to start a wrapped line, not a list marker, and must not match.
var LIST_ITEM = /^([\-\*\u2022\u00b7\u25cf\u25aa\u25e6\u2013\u2014]|\d+[.)]|\(\s*(?:\d{1,3}|[a-z]{1,4})\s*\)|[a-zA-Z][.)])\s+/;
var SENTENCE_END = /[.!?:;\u3002\uff01\uff1f\uff1a\uff1b][\"'\u201d\u2019\)\]]*$/;
var TRAILING_HYPHEN = /[\-\u2010]$/;
var LOWERCASE_HEAD = /^[a-z\u00e0-\u00ff]/;
var CJK_TAIL = /[\u2e80-\u9fff\uf900-\ufaff\uff00-\uffef]$/;
var CJK_HEAD = /^[\u2e80-\u9fff\uf900-\ufaff\uff00-\uffef]/;
var CJK_CHAR = /[\u2e80-\u9fff\uf900-\ufaff\uff00-\uffef]/;

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
// How many of the most recently merged lines' widths to keep as the local
// column estimate - see the note on WINDOW_SIZE / SURGE_RATIO below.
var WINDOW_SIZE = 4;
// A line (or the region it belongs to) whose width crosses this multiple of
// the current column is read as a font-size change, not a wrapped line.
var SURGE_RATIO = 1.6;
// How close the line *after* a width jump has to land to the jumped-to width
// for that jump to count as the start of a persisting new region, rather
// than a single line that just happened not to get wrapped.
var PERSIST_RATIO = 0.3;

// A CJK character takes up roughly twice the horizontal space of a Latin
// one, so column-width comparisons use this instead of raw character
// count - otherwise MIN_COLUMN/MAX_COLUMN, tuned for Latin prose, would
// almost never trigger on CJK text: a wrapped Chinese paragraph rarely
// reaches 45 raw characters per line even though it fills the same width.
function visualWidth(text) {
    var width = 0;
    for (var i = 0; i < text.length; i++) {
        width += CJK_CHAR.test(text.charAt(i)) ? 2 : 1;
    }
    return width;
}

function medianWidth(widths) {
    if (!widths.length) {
        return 0;
    }
    var sorted = widths.slice().sort(function (a, b) { return a - b; });
    var mid = Math.floor(sorted.length / 2);
    return sorted.length % 2
        ? sorted[mid]
        : Math.round((sorted[mid - 1] + sorted[mid]) / 2);
}

// A screen capture can catch a large-font heading directly above small-font
// body text, with no blank line between them - the heading's lines are
// naturally much narrower than the body's, so no single column width can
// judge both correctly. A jump away from the current column only means the
// text entered a different region (heading <-> body) if the line *after*
// the jump keeps roughly that new width too; a single freak-width line
// surrounded by normal ones (a PDF copy that happened to drop one line
// break) is not a region change and should still be free to merge.
function entersNewRegion(column, nextWidth, afterWidth) {
    if (!column || nextWidth <= column * SURGE_RATIO) {
        return false;
    }
    if (afterWidth === undefined) {
        return true;
    }
    if (afterWidth > column * SURGE_RATIO) {
        return true;
    }
    // If the line after the jump doesn't keep anywhere near the jumped-to
    // width, the jump was a one-off (e.g. a PDF copy that happened to drop
    // one line break), not the start of a persisting new region.
    return Math.abs(afterWidth - nextWidth) <= nextWidth * PERSIST_RATIO;
}

function unwrapText(text) {
    if (!text || text.indexOf("\n") < 0) {
        return text;
    }

    var raw = text.replace(/\r\n|\r/g, "\n").split("\n");
    var lines = [];
    var widths = [];
    var maxWidth = 0;
    var nonEmptyWidths = [];
    var i;

    for (i = 0; i < raw.length; i++) {
        var trimmed = trimString(raw[i]);
        lines.push(trimmed);
        var width = trimmed ? visualWidth(trimmed) : 0;
        widths.push(width);
        if (width > maxWidth) {
            maxWidth = width;
        }
        if (trimmed) {
            nonEmptyWidths.push(width);
        }
    }

    // A single line past MAX_COLUMN means the text arrives already joined
    // (or was pasted from somewhere without wrapping at all) - leave
    // everything alone.
    if (maxWidth > MAX_COLUMN) {
        return text;
    }

    // Only bother if at least one line looks like it could be wrapped prose.
    var qualifies = false;
    for (i = 0; i < nonEmptyWidths.length; i++) {
        if (nonEmptyWidths[i] >= MIN_COLUMN) {
            qualifies = true;
            break;
        }
    }
    if (!qualifies) {
        return text;
    }

    // Used as the column estimate whenever there isn't yet a local window to
    // measure from (the very first line of a paragraph, or right after a
    // break resets it) - and for judging whether a *closed* paragraph reads
    // as a heading, since by then its own width no longer reflects the
    // column it was wrapped at (a merged multi-line heading is long).
    var globalColumn = medianWidth(nonEmptyWidths);

    var out = [];
    var previousLine = "";
    // Widths of the lines actually merged into the run in progress, most
    // recent last - this is the *local* column estimate. It only accumulates
    // lines that were merged, and resets on every new paragraph, so a
    // heading's narrow lines can never leak into the body's column or vice
    // versa (see entersNewRegion for why the region boundary itself doesn't
    // rely on this being populated yet).
    var window = [];

    for (i = 0; i < lines.length; i++) {
        var line = lines[i];

        if (!line) {
            closeParagraph(out, globalColumn, false);
            pushBlank(out);
            previousLine = "";
            window = [];
            continue;
        }

        var lineWidth = widths[i];
        var startsBlock = !out.length || out[out.length - 1] === "";

        if (startsBlock) {
            out.push(line);
            previousLine = line;
            window = [];
            continue;
        }

        var column = window.length ? medianWidth(window) : globalColumn;
        var afterWidth = (i + 1 < widths.length && lines[i + 1]) ? widths[i + 1] : undefined;
        var regionChange = entersNewRegion(column, lineWidth, afterWidth);

        if (regionChange || keepsBreak(previousLine, line, column)) {
            closeParagraph(out, globalColumn, regionChange);
            out.push(line);
            previousLine = line;
            window = [];
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
        window.push(lineWidth);
        if (window.length > WINDOW_SIZE) {
            window.shift();
        }
    }

    return out.join("\n");
}

function pushBlank(out) {
    if (out.length && out[out.length - 1] !== "") {
        out.push("");
    }
}

// Give the paragraph that just closed a blank line of its own if it reads
// as a heading, so the structure survives into the translation. A region
// change ending it is itself a strong enough signal on its own (a merged
// heading can be as long as the body text next to it, so its own width
// stops being useful for this call); otherwise fall back to the plain
// "was this line short next to the column it was wrapped at" test.
function closeParagraph(out, column, regionChange) {
    var text = out.length ? out[out.length - 1] : "";
    if (!text) {
        return;
    }
    if (SENTENCE_END.test(text) || LIST_ITEM.test(text)) {
        return;
    }
    if (regionChange || visualWidth(text) < column * HEADING_RATIO) {
        pushBlank(out);
    }
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
    // character (2 width units) is enough to show there was room left.
    var unit = CJK_HEAD.test(next) ? 2 : visualWidth(firstWord(next));
    return visualWidth(previousLine) + 1 + unit <= column - WIDTH_SLACK;
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
