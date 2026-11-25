import { Language } from '../../domain/language';

/**
 * User-facing texts for the deterministic notice codes produced by the APIs, as [EN, DE, TR]. Codes are stable
 * and language-independent. Unknown codes fall back to a generic text, so a new backend code never shows raw
 * internals.
 */
type Texts = readonly [en: string, de: string, tr: string];

const NOTICES: Readonly<Record<string, Texts>> = {
  CREDIT_READ_REQUIRED: [
    'Your role does not include access to credit applications.',
    'Ihre Rolle umfasst keinen Zugriff auf Kreditanträge.',
    'Rolünüz kredi başvurularına erişimi kapsamıyor.',
  ],
  CREDIT_APPLICATION_NOT_ASSIGNED: [
    'This credit application is not assigned to you.',
    'Dieser Kreditantrag ist Ihnen nicht zugewiesen.',
    'Bu kredi başvurusu size atanmamış.',
  ],
  CREDIT_NOTE_PERMISSION_REQUIRED: [
    'Your role may read this application but not add notes to it.',
    'Ihre Rolle darf diesen Antrag lesen, aber keine Notizen hinzufügen.',
    'Rolünüz bu başvuruyu okuyabilir ama not ekleyemez.',
  ],
  CREDIT_APPROVE_REQUIRED: [
    'Your role does not include approving credit applications.',
    'Ihre Rolle umfasst keine Genehmigung von Kreditanträgen.',
    'Rolünüz kredi başvurusu onaylamayı kapsamıyor.',
  ],
  CREDIT_APPROVAL_LIMIT_EXCEEDED: [
    'The amount exceeds your approval limit.',
    'Der Betrag überschreitet Ihr Genehmigungslimit.',
    'Tutar onay limitinizi aşıyor.',
  ],
  CREDIT_FOUR_EYES_REQUIRED: [
    'You prepared this application, so someone else has to approve it (four-eyes principle).',
    'Sie haben diesen Antrag vorbereitet, daher muss jemand anderes ihn genehmigen (Vier-Augen-Prinzip).',
    'Bu başvuruyu siz hazırladınız; başka biri onaylamalı (dört göz ilkesi).',
  ],
  CREDIT_APPLICATION_NOT_APPROVABLE: [
    'This application is not in a state that can be approved.',
    'Dieser Antrag ist nicht in einem genehmigungsfähigen Status.',
    'Bu başvuru onaylanabilecek bir durumda değil.',
  ],
  REQUEST_PARTIALLY_UNAVAILABLE: [
    'Part of this information is not available to you.',
    'Ein Teil dieser Informationen ist für Sie nicht verfügbar.',
    'Bu bilginin bir kısmı sizin için kullanılabilir değil.',
  ],
  ASSISTANT_USE_REQUIRED: [
    'Your role does not include using the assistant.',
    'Ihre Rolle umfasst keine Nutzung des Assistenten.',
    'Rolünüz asistanı kullanmayı kapsamıyor.',
  ],
  ASSISTANT_MACHINE_PRINCIPAL_NOT_ALLOWED: [
    'Service accounts cannot use the assistant.',
    'Dienstkonten können den Assistenten nicht nutzen.',
    'Servis hesapları asistanı kullanamaz.',
  ],
  ASSISTANT_UNKNOWN_TOOL: [
    'The assistant asked for an action that does not exist; it was ignored.',
    'Der Assistent wollte eine nicht existierende Aktion ausführen; sie wurde ignoriert.',
    'Asistan var olmayan bir işlem istedi; yok sayıldı.',
  ],
  ASSISTANT_TOOL_NOT_PERMITTED: [
    'Part of your request needs access your role does not include.',
    'Ein Teil Ihrer Anfrage erfordert Zugriff, den Ihre Rolle nicht hat.',
    'İsteğinizin bir kısmı rolünüzün kapsamadığı bir erişim gerektiriyor.',
  ],
  ASSISTANT_INVALID_TOOL_ARGUMENTS: [
    'Part of your request could not be understood (invalid identifier or text).',
    'Ein Teil Ihrer Anfrage war nicht verständlich (ungültige Kennung oder Text).',
    'İsteğinizin bir kısmı anlaşılamadı (geçersiz kimlik veya metin).',
  ],
  ASSISTANT_TOOL_CALL_LIMIT_REACHED: [
    'The request needed too many steps; only the first ones were carried out.',
    'Die Anfrage brauchte zu viele Schritte; nur die ersten wurden ausgeführt.',
    'İstek çok fazla adım gerektirdi; yalnızca ilk adımlar yapıldı.',
  ],
  ASSISTANT_WRITE_LIMIT_REACHED: [
    'The assistant may save only one note per request.',
    'Der Assistent darf pro Anfrage nur eine Notiz speichern.',
    'Asistan istek başına yalnızca bir not kaydedebilir.',
  ],
  ASSISTANT_HUMAN_CONFIRMATION_REQUIRED: [
    'The assistant never carries this out itself; it needs your explicit confirmation on the page.',
    'Der Assistent führt dies nie selbst aus; es braucht Ihre ausdrückliche Bestätigung auf der Seite.',
    'Asistan bunu asla kendisi yapmaz; sayfada sizin açık onayınız gerekir.',
  ],
  ASSISTANT_TOOL_FAILED: [
    'Part of your request failed for technical reasons. Please try again.',
    'Ein Teil Ihrer Anfrage ist technisch fehlgeschlagen. Bitte erneut versuchen.',
    'İsteğinizin bir kısmı teknik bir nedenle başarısız oldu. Lütfen tekrar deneyin.',
  ],
  ASSISTANT_MODEL_UNAVAILABLE: [
    'The language model is currently unavailable.',
    'Das Sprachmodell ist derzeit nicht verfügbar.',
    'Dil modeli şu anda kullanılamıyor.',
  ],
  ASSISTANT_NO_DOCUMENT_EVIDENCE: [
    'No sufficiently relevant authorized document source was found. The assistant did not guess.',
    'Keine ausreichend relevante, freigegebene Dokumentquelle gefunden. Der Assistent hat nicht geraten.',
    'Yeterince ilgili, yetkili bir belge kaynağı bulunamadı. Asistan tahmin yürütmedi.',
  ],
  ASSISTANT_NO_VERIFIED_DATA: [
    'No authorized bank data was retrieved to support an answer.',
    'Es wurden keine freigegebenen Bankdaten als Grundlage abgerufen.',
    'Yanıtı destekleyecek yetkili banka verisi getirilmedi.',
  ],
  ASSISTANT_NO_VERIFIED_FINANCIAL_DATA: [
    'No authorized account-balance summary was retrieved. A customer list is not evidence of a balance.',
    'Keine freigegebene Saldenübersicht abgerufen. Eine Kundenliste ist kein Beleg für einen Saldo.',
    'Yetkili bir bakiye özeti getirilmedi. Müşteri listesi bir bakiyenin kanıtı değildir.',
  ],
};

const GENERIC_FORBIDDEN_TEXTS: Texts = [
  'You are not allowed to perform this action.',
  'Sie dürfen diese Aktion nicht ausführen.',
  'Bu işlemi yapmaya yetkiniz yok.',
];

/** English fallback text (kept as an export for callers and tests). */
export const GENERIC_FORBIDDEN = GENERIC_FORBIDDEN_TEXTS[0];

const INDEX: Readonly<Record<Language, number>> = { en: 0, de: 1, tr: 2 };

export function noticeText(code: string | null, language: Language = 'en'): string {
  return ((code && NOTICES[code]) || GENERIC_FORBIDDEN_TEXTS)[INDEX[language]];
}
