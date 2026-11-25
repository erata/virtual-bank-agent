#!/usr/bin/env python3
"""Reproducible, SYNTHETIC VirtualBank RAG fixture expansion (DOC-116..DOC-130).

Only corpus authoring needs reportlab and python-docx; running the published stack
uses the committed PDF/DOCX/MD/TXT/JSON fixtures directly, without Python packages.
Usage: python -m pip install reportlab python-docx
       python tools/generate_extended_corpus.py

All people, prices, policy thresholds, dates and rules are fictional demonstration data.
"""
import json
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'sample-docs'

# The sections deliberately distinguish versions, languages, and classification levels.
# The PDFs have separate authored sections per page; not duplicate padding.
CORPUS = [
    dict(id=116,path='public/fees-2024-archive.pdf',lang='en',title='Archived Retail Fee Schedule 2024',level=0,
         pages=[
         ('Historical status and scope', [('Archive marker','ARCHIVED 2024 FEE SCHEDULE. This schedule is historical and must not be used to quote current retail fees. It applied from 1 January 2024 through 31 December 2024 to fictional demo retail accounts.'),('Standard account','The archived monthly standard account charge was EUR 5.90. Basic digital transfers were free. Paper transfers had a EUR 2.50 service charge.'),('Transparency','An advisor should explicitly identify this archive as superseded, verify the effective date in each answer and redirect a 2026 customer to the current schedule DOC-117.')]),
         ('Historical transfers and cash', [('SEPA transfers','The historical instant SEPA transfer fee was EUR 0.80 per eligible transaction. Regular online SEPA transfers were free; this price is no longer current.'),('Foreign currency','A hypothetical historical FX administration charge of 1.30 percent applied to selected transfers. Exchange rates themselves were set separately and are not present in this document.'),('ATM service','The archived third-party ATM fee schedule used a minimum EUR 4.00; actual third-party charges could vary.')]),
         ('Historic exemptions and dispute handling',[('Student plan','The archived student account waived the EUR 5.90 standard monthly charge until age 25. The exemption is only an illustration of the old pricing structure.'),('Fee complaints','Customers could request a fee explanation through their advisor. The historical dispute window was not the same as a card chargeback deadline.'),('Citation rule','For questions containing 2024, archival, earlier, previous or historical, cite DOC-116. For current, 2026 or latest prices, cite DOC-117.')]),
         ]),
    dict(id=117,path='public/fees-2026-current.pdf',lang='en',title='Current Retail Fee Schedule 2026',level=0,
         pages=[
         ('Valid 2026 fee rules',[('Effective date','CURRENT 2026 FEE SCHEDULE. Fictional demo rules are effective from 1 January 2026. They supersede the archived 2024 conditions in DOC-116.'),('Standard account','The current standard monthly service charge is EUR 4.90. Electronic account statements and ordinary online SEPA transfers cost EUR 0.'),('Special accounts','The demo student plan has no monthly maintenance fee until age 27, provided enrollment is verified by the bank. No automatic waiver is inferred from user age alone.')]),
         ('Transfers and international payments',[('Instant SEPA','Current instant SEPA transfer charges are EUR 0.00 for eligible consumer online transactions. This is different from DOC-116, which recorded EUR 0.80 in 2024.'),('Paper orders','The service charge for paper-initiated transfer instructions is EUR 2.00 in the 2026 demo policy. A separate payment authorization is always required.'),('Foreign exchange','Foreign currency conversion may include a 1.00 percent demo handling charge. A real FX rate is not included; an AI assistant must not invent a EUR/USD or EUR/TRY rate.')]),
         ('Customer communication',[('When to quote fees','For questions about current fees, answer only with the effective 2026 price and cite DOC-117. Mark values as synthetic demo policy rather than real bank terms.'),('Refund requests','A customer can ask for an explanation but the assistant has no refund execution tool. It must not promise an account adjustment without a dedicated authorized use case.'),('Accounts','Account owners can ask the assistant for this public schedule after a valid login. Public visibility does not grant access to internal credit, salary, operations or staff documents.')]),
         ]),
    dict(id=118,path='public/sepa-transfers-guide-tr.pdf',lang='tr',title='SEPA Havale ve Ödeme Rehberi 2026',level=0,
         pages=[
         ('SEPA ödeme esasları',[('Genel tanım','SEPA, Avro cinsinden transferler için tasarlanmış bir ödeme alanıdır. Bu kurgusal VirtualBank rehberi ödeme oluşturma işlevi sunmaz; yalnızca açıklayıcı müşteri bilgisidir.'),('IBAN doğrulaması','İşlem öncesinde alıcının IBAN bilgisi biçimsel olarak denetlenir. Biçimsel doğrulama hesabın gerçekten alıcıya ait olduğunu kanıtlamaz.'),('Kimlik ve yetki','Banka müşterisi kendi hesabına bağlı bilgileri görebilir. Başka müşterinin IBAN bilgisini görmek için sohbet komutu yeterli değildir; uygulama politikası ve PostgreSQL RLS kontrolü gerekir.')]),
         ('Ödeme türleri',[('Standart SEPA','Standart SEPA havalesi ile anlık SEPA transferi farklı hizmetlerdir. İşlemin hızını ilgili ödeme sistemi, bankanın destek durumu ve gerekli doğrulamalar belirler.'),('Zamanlama','Önümüzdeki 5 veya 30 gün için planlanan ödemeler tarih aralığına göre listelenebilir. Yapılacak ödeme ile tamamlanmış ödeme hareketi ayrı sorgulardır.'),('Çoklu para birimi','EUR, USD ve TRY hesap bakiyeleri kur çevrimi olmadan tek bir parasal tutarda birleştirilmez. Her para birimi ayrı gösterilmelidir.')]),
         ('Müşteri güvenliği',[('Otomatik ödeme','Dauerauftrag olarak da bilinen düzenli ödeme talimatı, her dönemde planlı işlem doğurabilir. Talimat kaydı, tamamlanan para transferinin kendisi değildir.'),('Onay adımı','Yeni havale, alıcı değiştirme ve ödeme talimatı yaratma gibi yazma işlemleri kullanıcı onayı, idempotency ve bağımsız yetkilendirme olmadan asistan üzerinden çalıştırılamaz.'),('Soru örnekleri','IBAN’ım nedir, gelecek ayın ödemelerini listele, otomatik ödeme talimatlarım neler soruları ancak doğrulanmış müşteri kimliği kapsamında yanıtlanabilir.')]),
         ]),
    dict(id=119,path='public/sepa-transfer-guide-de.pdf',lang='de',title='SEPA Überweisungen und Daueraufträge 2026',level=0,
         pages=[
         ('Überweisungen im Überblick',[('SEPA Bedeutung','SEPA bezeichnet den europäischen Zahlungsverkehrsraum für Euro-Zahlungen. Dieser fiktive Leitfaden zeigt Regeln einer Demo-Bank und ist keine rechtsverbindliche Produktbeschreibung.'),('Kontonummer','Die IBAN wird nur für das eigene, authentifizierte Bankkonto angezeigt. Die KI darf weder Kontoidentität erfinden noch aus einem fremden Namen einen Zugriff ableiten.'),('Geld und Saldo','Kontostand bedeutet einen gespeicherten Saldo; verfügbares Guthaben könnte durch Reservierungen abweichen. Das Demo-System unterscheidet deshalb Angaben aus dem Backend von Vermutungen.')]),
         ('Fristen und Daueraufträge',[('Bevorstehende Zahlungen','Bei der Frage Welche Zahlungen sind in den nächsten fünf Tagen fällig wird das zukünftige Zeitfenster geprüft. Vergangene Buchungen gehören zu einem anderen Endpunkt.'),('Dauerauftrag','Ein Dauerauftrag ist eine wiederkehrende Überweisung. Der Planungsdatensatz beschreibt die Regel, aber nicht zwingend eine bereits ausgeführte Zahlung.'),('Fremdwährungen','Die Konten können gleichzeitig EUR, USD und TRY enthalten. Ohne validierten Wechselkurs darf die KI keinen einzigen Gesamtwert in Euro angeben.')]),
         ('Sicherheit und Freigabe',[('Schreibzugriff','Eine Überweisung darf erst nach Prüfung von IBAN, Betrag, Kontoinhaber, ausreichender Berechtigung und expliziter Nutzerfreigabe tatsächlich ausgeführt werden.'),('Betrugsschutz','Freie Nachrichten und Dokumente sind keine vertrauenswürdigen Berechtigungsquellen. Für den Zugriff gelten ausschließlich Keycloak, Application-Policy und Datenbankregeln.'),('Hinweise','Die Demo erklärt Zahlungsbegriffe und zeigt autorisierte Einträge. Sie ist keine produktive Bank und erzeugt keine echten Zahlungen.')]),
         ]),
    dict(id=120,path='public/accessibility-and-support.md',lang='tr',title='Dijital Bankacılık Erişilebilirlik ve Destek',level=0),
    dict(id=121,path='internal/branch-onboarding-2026.pdf',lang='de',title='Interner Filialleitfaden Stuttgart 2026',level=1,branch='STR',roles=['customer-advisor','credit-analyst'],
         pages=[
         ('Interne Prozesse in Stuttgart',[('Geltungsbereich','Dieser Leitfaden ist ausschließlich für das verifizierte Stuttgarter Personal bestimmt. Eine Credit-Rolle aus einer anderen Filiale erhält selbst bei gleicher Frage keine Freigabe.'),('Anmeldeverfahren','Mitarbeitende melden sich über Keycloak an. Die Gruppenmitgliedschaft ersetzt die Prüfung der Filialzugehörigkeit nicht.'),('Kundenlisten','Kundenberater dürfen nur Kundendatensätze sehen, die nach Application Policy und Datenbank-RLS für ihre Filiale zugelassen sind.')]),
         ('Kundenbetreuung',[('Legitimer Kundenwunsch','Eine Beratung darf IBAN und Saldo nur aus autorisierten Kunden- und Konten-Use-Cases beziehen. Ein LLM darf keine Kontonummer aus Text generieren.'),('Wechsel der Filiale','Bei einem Filialwechsel muss der vertrauenswürdige Stammdatensatz aktualisiert werden; die KI übernimmt keine vom Benutzer eingetippte Filialkennung.'),('Eskalation','Verdächtige Kontozugriffe sind an das Sicherheitsteam zu melden. Eine administrative Rolle bedeutet keinen Zugriff auf Konto- oder HR-Inhalte.')]),
         ('Sichere KI Nutzung',[('ToolPolicyGate','Der Agent darf nur freigegebene lesende Bank-Tools ausführen. Kreditfreigaben und Schreibaktionen bleiben bis zu einem geprüften Human-in-the-Loop-Prozess deaktiviert.'),('Dokumentsuche','Öffentliche und interne Dokumente werden nach Clearance, ACL und Filiale gefiltert, bevor ein Ausschnitt in den Modellkontext gelangt.'),('Audit','Ein Zugriffsverstoß soll einen deterministischen Hinweis erzeugen, ohne die Existenz gesperrter Kundendaten preiszugeben.')]),
         ]),
    dict(id=122,path='credit/approval-policy-2023-archive.docx',lang='en',title='Archived Credit Approval Policy 2023',level=2,group='/Credit'),
    dict(id=123,path='credit/approval-policy-2026-current.docx',lang='de',title='Aktuelle Kreditfreigaberichtlinie 2026',level=2,group='/Credit'),
    dict(id=124,path='credit/exception-workflow-2026-tr.md',lang='tr',title='Kredi İstisna Onay Akışı 2026',level=2,group='/Credit'),
    dict(id=125,path='credit/secured-lending-2026.md',lang='en',title='Secured Lending Collateral Procedures 2026',level=2,group='/Credit'),
    dict(id=126,path='hr/compensation-review-2026.docx',lang='en',title='HR Compensation Review Framework 2026',level=3,roles=['hr-specialist']),
    dict(id=127,path='operations/incident-severity-matrix-2026.docx',lang='de',title='Incident Severity und Eskalation 2026',level=2,roles=['operations-operator']),
    dict(id=128,path='operations/backup-recovery-2026.txt',lang='tr',title='Yedekleme ve Kurtarma Tatbikatı 2026',level=2,roles=['operations-operator']),
    dict(id=129,path='public/card-disputes-faq-2026.txt',lang='en',title='Card Disputes Customer FAQ 2026',level=0),
    dict(id=130,path='public/fee-faq-2026.json',lang='de',title='Gebühren FAQ 2026',level=0),
]

DETAILS = {
120: [
 ('Erişilebilir kanal', 'VirtualBank demosunda müşteri yardım sayfaları Türkçe, Almanca ve İngilizce terimler içerir. Erişilebilirlik gereksinimleri için metin bazlı alternatifler sağlanması hedeflenir. Asistanın verdiği bilgi, yetkili API kayıtları ve kaynaklı kamu dokümanlarıyla sınırlıdır.'),
 ('Müşteri belgeleri', 'Hesap ürünleri, ücret listesi ve genel SEPA rehberleri public sınıflandırmalı belgelerdir. Banka müşterisinin document.read yetkisi yalnızca public clearance ile birlikte kullanılır; dahili müşteri kayıtlarına veya kredi politikalarına erişim doğurmaz.'),
 ('Şifre değiştirme', 'Kennwort, Passwort, password ve şifre benzer amaçları ifade eder. Ancak asistanın şifreyi değiştirme toolu bulunmuyorsa işlem yaptığını iddia etmemeli; mevcut giriş sistemindeki güvenilir şifre yenileme ekranına yönlendirmelidir.'),
 ('Bilgi doğruluğu', 'Belge araması sonucunda ilgili kaynak bulunamazsa sistem politika uydurmamalı; yetkili kaynağın bulunamadığını bildirmelidir. Farklı para birimlerindeki hesap tutarlarını gerçek döviz kuru olmadan toplamamalıdır.'),
 ('Destek kapsamı', 'Bu belgeler yalnızca sentetik eğitim verileridir. Acil dolandırıcılık bildirimi, gerçek ödeme başlatma ve kart bloke işlemleri demo asistanı tarafından yerine getirilmez.'),
],
122: [
 ('Historical scope', 'ARCHIVED CREDIT APPROVAL POLICY 2023. This historical fixture expired on 31 December 2023. It is not the current policy for 2026 applications and must not be used to justify a current approval.'),
 ('Former approval band', 'The historical 2023 fictional policy asked for a second reviewer at EUR 30,000; a credit analyst could draft a recommendation but could not approve an assigned request alone. The old amount differs from the 2026 policy.'),
 ('Previous exception path', 'During 2023, deviations from standard scoring required an exception note and approval by the regional manager within two business days. These are sample numbers to test temporal document retrieval.'),
 ('Access control', 'Only the authorized /Credit group can access this confidential archived document. Archived status does not override resource policy or database row-level security.'),
 ('Response quality', 'When asked for current approval policy, retrieve DOC-123 instead. When asked explicitly for the superseded 2023 threshold, cite DOC-122 with an archive warning.'),
],
123: [
 ('Gültigkeit', 'AKTUELLE KREDITFREIGABERICHTLINIE 2026. Diese fiktive Richtlinie gilt ab 1. Januar 2026. Die frühere Version aus 2023 (DOC-122) ist archiviert und darf aktuelle Entscheidungen nicht begründen.'),
 ('Vier-Augen-Prinzip', 'Bei einer Demo-Kreditsumme ab 50.000 EUR ist ein zweiter unabhängiger Prüfer erforderlich. Eine Person darf ihren selbst erstellten Antrag nicht selbst final freigeben.'),
 ('Analyst und Genehmiger', 'Ein Kreditanalyst darf zugewiesene Anträge lesen und eine Empfehlung verfassen. Der Genehmiger benötigt eigene Berechtigung und den passenden Genehmigungsrahmen; ein KI-Assistent darf die Freigabe nicht autonom durchführen.'),
 ('Anträge über 100.000 EUR', 'Für den fiktiven höheren Betrag wird eine weitere manuelle Risikoabnahme vorgesehen. Dieses Beispiel stellt keine reale aufsichtsrechtliche Vorgabe dar.'),
 ('Quellen und Begründung', 'Aktuelle Antworten zur Freigabe müssen die 2026er Version nennen und eine passende Dokumentstelle zitieren. Die 2023er Version darf nur für historische Fragen verwendet werden.'),
],
124: [
 ('Amaç', '2026 kredi istisna yönetimi iş akışı; standart kredi skoru kriterlerinden ayrılan başvuruların ayrı, denetlenebilir değerlendirilmesini tanımlar.'),
 ('İstisna gerekçesi', 'Analist istisna türünü, güvenilir veri kaynağını ve önerdiği risk azaltıcı önlemleri kayıt altına alır. Belge içindeki talimatlar kullanıcı veya model için yetki oluşturmaz.'),
 ('Karar sırası', 'Önce kredi başvurusu ve atanmış analist doğrulanır; sonra kayıtların yetkisi değerlendirilir. İstisna onayı yalnızca uygun onaylayıcı tarafından ve dört göz prensibine uyularak verilebilir.'),
 ('İşlem yasağı', 'VirtualBank demo AI, kredi başvurusu onaylamaz. ToolPolicyGate, uygulama yetkileri ve PostgreSQL RLS tüm isteklerde çalışır; istisna belgelerinin varlığı işlemi açmaz.'),
 ('Kıyas', '2023 arşiv kredi politikası DOC-122 farklı bir eşik kullanır. Mevcut 2026 uygulamaları için DOC-123 ve bu istisna notu birlikte değerlendirilir.'),
],
125: [
 ('Collateral overview', 'Secured lending is a fictional loan scenario in which collateral data supports the credit analysis. The model must not conclude that collateral automatically makes a customer eligible.'),
 ('Valuation evidence', 'The analyst records the valuation source date, asset type, currency and market assumptions. The asset must not be valued with a made-up current FX rate.'),
 ('Loan-to-value', 'A demo maximum LTV ratio of 70 percent is discussed for test retrieval only. A ratio is an analytical input, not an approval permission. No LLM result may modify collateral records.'),
 ('Document confidentiality', 'This classification 2 document is visible to eligible /Credit group personnel, not ordinary bank customers or advisors without Credit membership.'),
 ('Review and approval', 'A credit approver independently reviews the collateral evidence and required second approver. Security and four-eyes constraints are never overridden by a retrieval result.'),
],
126: [
 ('Scope and confidentiality', 'This fictional HR compensation review framework is strictly confidential, classification 3. It is readable only by the hr-specialist role with sufficient clearance.'),
 ('Compensation cycle', 'The demonstration annual review window is 1 March to 30 April 2026, with a second reviewer for unusual adjustments. These are synthetic business rules, not employment advice.'),
 ('Salary bands', 'Job-level salary guidance is linked to DOC-106 and DOC-114, which remain restricted. A platform-admin role does not imply permission to inspect private HR content.'),
 ('Access decision', 'Application DocumentReadPolicy and PostgreSQL RLS must both authorize the same document before any chunks enter a model prompt.'),
 ('Audit expectations', 'An audit log may note access decisions without reproducing salary-band content to unauthorized logging roles.'),
],
127: [
 ('Ziel und Prioritäten', 'Die fiktive Schweregradmatrix unterscheidet SEV1, SEV2 und SEV3. SEV1 bedeutet kritische Beeinträchtigung des Zahlungsdienstes; SEV2 steht für bedeutende Teilausfälle.'),
 ('Erste Maßnahmen', 'Bei SEV1 wird die Bereitschaft sofort informiert und ein Incident-Koordinator benannt. Vor Änderungen müssen Logs und Zeitstempel gesichert werden.'),
 ('Recovery Time', 'Das Demo-Ziel für Wiederanlauf (RTO) beträgt für kritische Dienste 60 Minuten. Das Ziel ist keine garantierte zugesagte Wiederherstellungszeit.'),
 ('Kommunikation', 'Statusinformationen werden vom Operations-Team freigegeben. Ein KI-Agent darf keine ungeprüften Incident-Meldungen an Kunden publizieren.'),
 ('Informationssicherheit', 'Der Incident-Leitfaden ist intern nur für operations-operator mit der passenden Freigabe zugänglich.'),
],
128: [
 ('Kurtarma hedefi', 'Bu kurgusal operasyon tatbikatında müşteri sistemleri için geri dönüş süresi hedefi RTO 60 dakika, kabul edilen veri kaybı hedefi RPO 15 dakika olarak belirlenmiştir.'),
 ('Yedekleme planı', 'Gecelik tam yedek, saatlik işlem günlüğü arşivi ve aylık geri yükleme denemesi örnek süreç olarak tanımlanmıştır.'),
 ('Kurtarma denemesi', 'Veritabanı geri yüklemesinde doğrulama hashleri, minimum yetkili hesaplar ve denetim izleri kontrol edilir. Yedekten dönmek işlemleri otomatik olarak onaylamaz.'),
 ('Yetki', 'Bu belge yalnızca operations-operator rolü ve sınıf 2 erişim seviyesine açık tutulur. HR ve kredi analistleri için görünür değildir.'),
 ('Olay sonrası', 'İyileştirme maddeleri olay numarasıyla takip edilir. Yapay zeka cevabı belgeyi özetleyebilir ama gerçek kurtarma komutu çalıştıramaz.'),
],
129: [
 ('Customer dispute', 'A card dispute is a request to review a card transaction. The public FAQ distinguishes card disputes from a scheduled SEPA transfer or standing order.'),
 ('What to collect', 'The customer may collect the transaction date, merchant name and amount. Do not enter card PIN, CVV or passwords into the virtual assistant.'),
 ('Chargeback limitations', 'This demo has no real chargeback submission tool. The assistant may explain common terms but must not claim that a case has been submitted.'),
 ('Security advice', 'If an unauthorized transaction is suspected, contact the official bank service channel. A public document is not evidence that a transfer actually happened.'),
 ('Language', 'The terms chargeback, Kartenreklamation and kart harcaması itirazı may refer to related support topics; actual legal timelines are not specified in this sample.'),
],
130: [
 ('Aktuelle Gebühren', 'Für die 2026 gültigen synthetischen Gebühren gilt DOC-117: Kontoführung 4,90 EUR pro Monat, Instant-SEPA online 0,00 EUR, Papierauftrag 2,00 EUR.'),
 ('Frühere Preise', 'Die Archivversion DOC-116 für 2024 enthielt 5,90 EUR monatliche Kontoführung und 0,80 EUR für Instant-SEPA. Historische Preise dürfen nicht als aktuelle Gebühren dargestellt werden.'),
 ('Kundenrechte', 'Der Kunde darf öffentliche FAQ und Gebühreninformationen mit einem authentifizierten Konto abrufen. Das erlaubt niemals das Lesen interner Kredit- oder Personalakten.'),
 ('Geltungsbereich', 'Alle Beträge sind ausdrücklich synthetische Beispiele für eine technische Referenzapplikation.'),
],
}


# Additional distinct operational examples make each PDF genuinely multi-section,
# multi-chunk reference material rather than one padded page per topic.
PAGE_SCENARIOS = {
 116: [
  [('Example: old statement review','A fictional retail customer asks why an account statement dated April 2024 includes a maintenance fee of EUR 5.90. The investigator first checks the statement posting date, then cites DOC-116 as historical context. The investigator must not infer that the fee still applies today. A normal current-fee question should instead be answered from DOC-117.'),
   ('Historical versus current search','Search indexes contain both old and new price schedules because customers can dispute earlier postings. The phrase old pricing, 2024 or archived should increase the relevance of this archive; current, latest and 2026 should prefer the replacement. Both records remain public, but retrieval ranking still needs an explicit date-sensitive evaluation.')],
  [('Example: instant transfer on an old invoice','Suppose a customer asks whether an instant transfer charged in October 2024 was consistent with the historical demonstration policy. The archive describes EUR 0.80, not the current zero-fee value. The assistant may explain the past document but may not determine that a real bank charged a fee without authorized transaction data.'),
   ('Cross-currency and dated transactions','The old foreign exchange percentage is a synthetic pricing illustration. It is not an exchange-rate feed. A bank account holding EUR, USD and TRY must display the three balances separately; applying this archived charge to calculate a present-day euro balance would be invalid and misleading.')],
  [('Example: disagreement across versions','A user may ask why DOC-116 says EUR 5.90 while DOC-117 says EUR 4.90. The correct answer is that the documents describe different effective periods. An RAG system should expose the date and status of each source instead of merging their amounts into a single statement.'),
   ('Historical evidence boundary','Text retrieved from an archived PDF is evidence only for what that PDF says, not permission to charge fees, modify balances or approve refunds. Any real adjustment would require a separately authorized, human-reviewed business operation. This demo does not expose that operation.')],
 ],
 117: [
  [('Effective-period selection','When a user asks about a charge for the current year, the assistant should retrieve this 2026 source even if an archived 2024 document shares the same fee-related vocabulary. Effective-date metadata and a clear current marker are intentional retrieval disambiguation signals. If the question omits its relevant date, the assistant should explain that the quoted 2026 figures are the currently documented synthetic demo values.'),
   ('Customer self-service boundaries','A logged-in bank customer may use this public fee document without gaining rights to internal operational policies. Customer-specific balance or transaction requests, by contrast, must use the verified customer identity and customer API: public fee-document access never allows broad customer account searches.')],
  [('Comparison with other payment products','The free 2026 instant online SEPA fee concerns the synthetic consumer transfer channel in this fixture; it does not automatically describe every business transfer, cross-border wire or card purchase. If a requested product has no explicit price in the accessible material, the assistant should report a lack of evidence rather than extrapolate.'),
   ('Amounts and currency','An amount of EUR 2.00 denotes a service fee, not a payment principal. Comparing fees to a customer’s USD or TRY account balances requires an authorized currency-conversion service, which the present demo does not provide. Responses should preserve both the value and its unit.')],
  [('FAQ scenario','A German-speaking user might ask Was kostet eine Echtzeitüberweisung or a Turkish-speaking user Anlık SEPA havalesi ücreti nedir. Both refer to the currently documented online instant-transfer charge of EUR 0.00. The public German fee FAQ DOC-130 and the English schedule DOC-117 provide overlapping evidence; a useful answer cites the exact current source.'),
   ('Document provenance','The effective date, document ID and title must travel with retrieved chunks into citations. If retrieval returns both 2024 and 2026 values, the assistant should make the version distinction explicit. It should not silently select a price because the corresponding chunk received a slightly higher cosine similarity score.')],
 ],
 118: [
  [('Örnek: gelecek ödemeler','Bir müşteri gelecek beş gün içinde hangi ödemeleri olduğunu sorarsa tarih aralığı bugünden başlayarak ileri yönde hesaplanmalıdır. Soru son beş gün olarak değişirse gerçekleşmiş işlem geçmişine bakılmalıdır. İki farklı sorgunun aynı tool ile cevaplanması işlemlerin durumunu yanlış göstermeye yol açabilir.'),
   ('Belgesel bilgi ve canlı veri','SEPA rehberindeki genel açıklamalar ile veritabanındaki gerçek demo hesap ve ödeme kayıtları farklı kaynaklardır. Belge tarihi ya da politika metni, o müşteriye ait ödeme gerçekleştiğini kanıtlamaz. Müşteri bakiyeleri yalnızca yetkilendirilmiş müşteri use case’leri üzerinden okunur.')],
  [('Örnek: otomatik ödeme talimatı','Deniz her ayın 15’inde 200 EUR kira ödenmesini sağlayan kurgusal bir düzenli talimat göstermek isteyebilir. Asistan, talimatın tanımını ve planlanan dönemi söyleyebilir; ancak son dönem ödemenin başarıyla gerçekleştiğini yalnızca işlem kayıtları doğrulayabilir. Talimatın varlığı ödenmiş tutar değildir.'),
   ('Para birimleri ve net bakiye','Müşterinin 500 EUR, 100 USD ve 1.000 TRY bakiyesi olması, bunları doğrudan 1.600 EUR olarak açıklamayı haklı çıkarmaz. Para birimine göre ayrıştırma yapılır; resmi kur, değer tarihi ve masraflar ayrı güvenilir kaynaklardan gelmedikçe para çevrimi yapılmaz.')],
  [('IBAN talimatının sınırı','Bir LLM kendisine verilen ad-soyad ya da müşteri numarasına göre IBAN oluşturamaz. Kullanıcının güvenilir JWT subject kimliği, hesap-sahipliği eşleşmesi ve veritabanı RLS birlikte doğrulanmadan IBAN gösterilmez. Başka müşterinin IBAN bilgisini sohbet içinde istemek bu sınırı değiştirmez.'),
   ('Dört göz ve onay','Bankacılıkta ödeme talimatı oluşturma yüksek etkili bir yazma işlemidir. Bu teknik demoda para transferi endpoint’i aktif değildir. İleride eklenirse önce kullanıcının para miktarı, alıcı, para birimi ve yürütme tarihi açıkça onaylanmalı; onay öncesi ve sonrasında yetki yeniden doğrulanmalıdır.')],
 ],
 119: [
  [('Beispiel: Kontostand abfragen','Die Frage Wie viel Geld habe ich auf meinen Konten ist ein bankfachlicher Datenabruf. Der Agent soll die eigenen Konten des eingeloggten Bankkunden laden und je Währung aggregieren. Die Begriffe Guthaben, Saldo, balance und bakiye können ähnlich gemeint sein, ersetzen aber weder Kontoinhaberprüfung noch Datenbankberechtigungen.'),
   ('Kein Zugriff über Namen','Ein Nutzer kann in einer Chatnachricht einen fremden Namen oder eine Kunden-ID nennen. Die Selbstbedienungsfunktionen bleiben dennoch an den signierten Keycloak-Subject und die zugehörige Kundentabelle gebunden. Die Auswahl der richtigen Funktion durch ein Sprachmodell allein stellt keine Zugriffserlaubnis dar.')],
  [('Beispiel: vergangene oder geplante Zahlung','Auf die Anfrage Welche Buchungen hatte ich in den letzten dreißig Tagen wird eine Abfrage abgeschlossener Transaktionen verwendet. Die Anfrage Was muss ich in den nächsten dreißig Tagen zahlen fragt nach geplanten Zahlungsterminen. Ein Modell muss Richtung, Dauer und Bezugspunkt korrekt bestimmen; bei Zweifeln soll es nachfragen.'),
   ('Dauerauftrag als Datentyp','Die gespeicherte Dauerauftragsregel enthält beispielsweise Empfänger, Rhythmus, Betrag und Währung. Eine Zahlungsereignis-Tabelle kann zusätzlich einzelne Ausführungen enthalten. Ein Assistent muss zwischen Regel, fälliger Zahlung und gebuchter Transaktion differenzieren.')],
  [('Berechtigte Dokumente','Authentifizierte Bankkunden besitzen für die öffentlichen Gebühren- und SEPA-Dokumente eine eingeschränkte Leseberechtigung. Ihre Clearance ist public und keine interne Mitarbeiterfreigabe. Dokumente mit classification 1, 2 oder 3 bleiben auch bei ähnlicher Frage und hohem semantischem Trefferwert unzugänglich.'),
   ('Technische Schutzschichten','Nach dem Funktionsaufruf prüfen ToolPolicyGate und Application Policy die Berechtigung, danach wird die Datenbankabfrage mit Row-Level Security ausgeführt. Für Dokumente muss ein zweiter unabhängiger ACL-Check vor dem Modellkontext stattfinden. Ein manipuliertes Dokument darf keine neuen Toolrechte vergeben.')],
 ],
 121: [
  [('Beratungsfall: Nachbarfiliale','Ein Kreditanalyst aus einer anderen Filiale fragt nach dem Stuttgarter Onboarding-Leitfaden. Selbst wenn die Rolle grundsätzlich interne Dokumente lesen kann, verweigert die Branch-Bedingung den Zugriff. Die fachliche Zuordnung der Filiale stammt ausschließlich aus vertrauenswürdigen Identitätsmerkmalen.'),
   ('Sicherheitsdokumente','Dokumente der Klassifikation internal dürfen nicht an Bankkunden oder administrative Nutzer ohne document.read gelangen. Die erlaubte Dokumentmenge hängt sowohl von der Berechtigung als auch von Dokument-ACL, Clearance und Filialbezug ab.')],
  [('Fall: Kundenwechsel','Ein Berater fragt nach einem Kunden aus einer anderen Filiale. Das System darf dessen Namen oder Kontodaten nicht über ein generisches Dokument-Suchergebnis rekonstruieren. Customer Use Cases geben nur freigegebene strukturierte Daten zurück; vertrauliche Geschäftsdaten sind keine Bestandteile des öffentlichen RAG-Korpus.'),
   ('Mehrsprachige Kommunikation','Im Beratungsalltag können Kunden Englisch, Türkisch oder Deutsch verwenden. Das Sprachmodell darf passende Toolnamen zuordnen, doch die resultierenden Daten müssen unverändert aus dem autorisierten Backend stammen. Übersetzte Antworten dürfen Beträge, IBANs und Betragswährungen nicht verändern.')],
  [('Protokollierung','Ein fehlgeschlagener Toolaufruf wird mit einer deterministischen Warnung und einem bereinigten Audit-Eintrag erfasst. Der Audit-Eintrag soll keine gesperrten Kontonummern, persönliche Daten oder vollständigen Dokumentabschnitte enthalten. Eine behauptete Genehmigung durch die KI hat keinerlei Auswirkung auf den Datenbankstatus.'),
   ('Review-Hinweis','Dieser Leitfaden ist nur ein realistisches synthetisches Beispiel. Die konkrete Trennung zwischen Kundensicht, Kreditdaten und HR-Informationen muss in der laufenden Implementierung durch unit tests, Keycloak-Claim-Prüfungen und PostgreSQL RLS-Integrationstests belegt werden.')],
 ],
}


def acl_for(d):
    if d.get('group'):
        return [{'subjectKind':'group','subjectValue':d['group'],'effect':'allow'}]
    if d.get('roles'):
        return [{'subjectKind':'role','subjectValue':r,'effect':'allow'} for r in d['roles']]
    return [{'subjectKind':'public','subjectValue':'*','effect':'allow'}]


def pdf_make(d, destination):
    from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak
    from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.lib import colors
    font='/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
    if not Path(font).is_file():
        raise RuntimeError('DejaVu font missing; only required when regenerating the docs fixture')
    if 'VBDejaVu' not in pdfmetrics.getRegisteredFontNames():
        pdfmetrics.registerFont(TTFont('VBDejaVu', font))
    title=ParagraphStyle('VBTitle',fontName='VBDejaVu',fontSize=16,leading=21,textColor=colors.HexColor('#123858'),spaceAfter=16)
    header=ParagraphStyle('VBHeader',fontName='VBDejaVu',fontSize=11,leading=16,textColor=colors.HexColor('#245d83'),spaceAfter=11)
    body=ParagraphStyle('VBBody',fontName='VBDejaVu',fontSize=9.4,leading=15,spaceAfter=13)
    note=ParagraphStyle('VBNote',fontName='VBDejaVu',fontSize=8.2,leading=13,textColor=colors.HexColor('#445563'))
    flow=[]
    for i,(section, details) in enumerate(d['pages'],1):
        if i>1: flow.append(PageBreak())
        flow.extend([Paragraph(escape(d['title']),title),Paragraph(escape(section),header),
                     Paragraph(f'{d["id"]:03d} / page {i} / Synthetic policy fixture / {d["lang"]}',note),Spacer(1,23)])
        for sub,content in details:
            flow.extend([Paragraph(escape(sub),header),Paragraph(escape(content),body),Spacer(1,6)])
        for sub,content in PAGE_SCENARIOS[d['id']][i-1]:
            flow.extend([Paragraph(escape(sub),header),Paragraph(escape(content),body),Spacer(1,6)])
        flow.append(Spacer(1,8))
        flow.append(Paragraph('NOT PRODUCTION BANKING ADVICE. This reference dataset contains fictional examples.',note))
    def footer(canvas,doc):
        canvas.setFont('VBDejaVu',8)
        canvas.setFillColor(colors.HexColor('#597084'))
        canvas.drawString(46,28,'VirtualBank synthetic evaluation corpus')
        canvas.drawRightString(549,28,f'DOC-{d["id"]:03d} • {doc.page}')
    SimpleDocTemplate(str(destination),pagesize=(595.28,841.89),rightMargin=48,leftMargin=48,topMargin=50,bottomMargin=50,
                      title=d['title'],author='VirtualBank Synthetic Corpus').build(flow,onFirstPage=footer,onLaterPages=footer)


def docx_make(d, destination):
    from docx import Document
    from docx.shared import Cm,Pt
    doc=Document(); sec=doc.sections[0]; sec.top_margin=Cm(2); sec.bottom_margin=Cm(2)
    sec.header.paragraphs[0].text='VirtualBank • synthetic training and retrieval corpus'
    doc.add_heading(d['title'],0)
    doc.add_paragraph(f'DOC-{d["id"]:03d} • {d["lang"].upper()} • 2026 reference fixture')
    for heading,text in DETAILS[d['id']]:
        doc.add_heading(heading,2)
        doc.add_paragraph(text)
    doc.add_paragraph('All rules, amounts and roles in this file are fictitious demonstration data.')
    sec.footer.paragraphs[0].text=f'VirtualBank | DOC-{d["id"]:03d} | non-production example'
    doc.save(destination)


def main():
    manifest_file=DOCS/'manifest.json'
    existing=json.loads(manifest_file.read_text(encoding='utf8'))
    legacy=[r for r in existing if int(r['id'].split('-')[1]) <=115]
    assert len(legacy)==15 and {int(r['id'].split('-')[1]) for r in legacy}==set(range(101,116))
    new=[]
    for d in CORPUS:
        dest=DOCS/d['path']; dest.parent.mkdir(parents=True,exist_ok=True)
        suffix=dest.suffix
        if suffix=='.pdf':pdf_make(d,dest)
        elif suffix=='.docx':docx_make(d,dest)
        elif suffix=='.json':
            dest.write_text(json.dumps({'documentId':f'DOC-{d["id"]:03d}','title':d['title'],'language':d['lang'],
                 'status':'synthetic-demo','faq':[{'question':h,'answer':v} for h,v in DETAILS[d['id']]]},indent=2,ensure_ascii=False)+'\n',encoding='utf8')
        else:
            sections=DETAILS[d['id']]
            dest.write_text(('# '+d['title']+'\n\n' if suffix=='.md' else d['title']+'\n\n')+
                '\n\n'.join(('## '+h+'\n\n' if suffix=='.md' else h+'\n')+v for h,v in sections)+
                '\n\nAll amounts and policies are fictional reference data.\n',encoding='utf8')
        new.append({'id':f'DOC-{d["id"]:03d}','title':d['title'],'path':d['path'],'language':d['lang'],
                    'classification':d['level'],'branch':d.get('branch'),'acl':acl_for(d)})
    manifest_file.write_text(json.dumps(legacy+new,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
    print('Generated 15 new documents; 30 total; five multi-page PDFs.')

if __name__=='__main__':main()
