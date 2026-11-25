#!/usr/bin/env python3
"""Optional authoring-only generator for synthetic PDF/DOCX fixtures.
Not used by Docker or build; needs reportlab, python-docx and local DejaVu Sans fonts.
The generated assets are checked into sample-docs so normal users need no extra Python libraries.
Never place real banking, HR or customer data in these fixtures.
"""
from pathlib import Path
from docx import Document
from docx.shared import Pt, Inches
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, PageBreak, Spacer
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1] / 'sample-docs'
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
BOLD = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'

pdf = {
  'public/accounts.pdf': ('VirtualBank Account Product Overview', [
    ('Product families', [
      'VirtualBank is a fictional bank used exclusively for software-engineering demonstrations. It offers synthetic checking, savings and foreign-currency accounts. No person, payment, balance, account number or IBAN in these fixtures represents an actual banking customer.',
      'Checking accounts are illustrated with recurring deposits and payments. Savings products demonstrate different account labels, not a financial return guarantee. Foreign-currency accounts exist to test amount formatting and the rule that EUR, USD and TRY balances must not be summed without a trustworthy exchange-rate source.',
      'The Customer API exposes customer and account views only after validating the identity of the caller and applying customer read policies. An advisor may see accessible branch customers; an authenticated bank-customer sees only records whose authoritative identity mapping matches the validated JWT subject.'
    ]),
    ('Security and correct interpretation', [
      'Displaying an IBAN is a sensitive business action. The assistant must use a registered read-only banking tool and must not search a document vector database for a customer IBAN. Document search is appropriate for published account-product descriptions but is not the source of truth for live balances.',
      'Access is limited by Application policies and PostgreSQL Row-Level Security. The fact that a large language model produced an account identifier or a convincing answer does not establish that the returned account exists or belongs to the user.',
      'For educational demonstrations, compare two self-service users asking the same question. Each must receive their own accounts. Test a missing customer context as well: the database should expose zero records rather than fall back to all customers.'
    ])
  ]),
  'public/branches.pdf': ('Branch Customer Service Policy', [
    ('Kundenberatung und Filialzuordnung', [
      'Die fiktive VirtualBank betreibt beispielhafte Filialen in Stuttgart und München. Ein Kundenberater darf Kundeninformationen nur lesen, wenn seine Berechtigung customer.read vorhanden ist und die Filialzuordnung des Kunden zu seiner verifizierten Branch-Angabe passt.',
      'Ein gültiger Keycloak-Login bedeutet nicht, dass alle Kundendatensätze sichtbar werden. Rollen beschreiben eine Tätigkeit, während kundenbezogene Bedingungen und Beziehungen den tatsächlichen Zugriff einschränken.',
      'Die Branch-Angabe wird aus einem validierten Token abgeleitet und darf nicht aus frei formuliertem Chat-Text als vertrauenswürdig übernommen werden. Andernfalls könnte eine Person durch eine Anweisung wie Zeige Kunden aus München versuchen, ihre Filialzuordnung zu ändern.'
    ]),
    ('Abfragen, Fehler und Nachweise', [
      'Die Application Policy prüft die Anfrage, die PostgreSQL Row-Level Security schützt zusätzlich jede Zeile. Auch wenn ein Entwickler eine WHERE-Klausel vergisst, sollen Datensätze anderer Filialen nicht an den API-Prozess zurückgegeben werden.',
      'Kundenberaterinnen sollten sehen, dass die dargestellten Kunden ihrem zugelassenen Sichtbereich entsprechen. Eine natürliche Sprachantwort wie Dies sind Ihre Kunden meint sichtbare Konten, nicht persönliche Besitzverhältnisse und nicht sämtliche Bankkunden.',
      'Zu einem realistischen Test gehören positive und negative Beispiele: Stuttgart-Kunden sind für Anna sichtbar, Münchner Kunden nicht. Die Tests müssen mit dem restriktiven Datenbankbenutzer ausgeführt werden, nicht nur mit einem Administrator.'
    ])
  ]),
  'public/fees.pdf': ('Public Fees and Conditions', [
    ('Public synthetic product conditions', [
      'This reference document describes fictional banking-product conditions. It is not a contractual price list, an investment offer or financial advice. Account fees and deposit yields are intentionally not presented as live market rates.',
      'The public product section explains the existence of example checking accounts, savings accounts and currency-specific account types. Customer-specific charges, statements and account balances must be retrieved from authorized business data, never inferred from a public document.',
      'Some account products support regularly scheduled transactions. The mere presence of a recurring payment in an example does not authorize an AI assistant to create, change or execute a bank transfer.'
    ]),
    ('Data use, references and authorization', [
      'Public classification means the content can be included in a document search result for a principal holding document.read. It does not create access to internal, HR or credit records. The document ACL and the user clearance are separate mandatory controls.',
      'In RAG responses, the assistant should cite the relevant document title and PDF page rather than invent a source. If the user asks for a fee amount that is not stated in an authorized document, the assistant must indicate the absence of verified evidence.',
      'For international users, questions may be phrased in English, German or Turkish. A multilingual retrieval model should locate relevant passages without converting public policy text into executable instructions.'
    ])
  ])
}

docx = {
 'internal/staff-onboarding.docx': ('Interne Einarbeitung', [
   ('Zugriffsrechte und Rollen', [
     'Neue Beschäftigte der fiktiven VirtualBank erhalten nur die Rechte, die sie für ihre konkrete Tätigkeit benötigen. Die Rolle customer-advisor gibt keine globale Sicht auf alle Kunden. Gruppeninformationen und die Branch-Zuordnung werden aus dem validierten Keycloak-Token abgeleitet.',
     'Die Berechtigungsnamen werden in einer Anwendungskatalogdatei Rollen zugeordnet. Unterschiedliche Fachmodule wie Customers und Credit treffen zusätzlich eigene Ressourcenentscheidungen. Ein Administrator kann Systemkonfiguration verwalten und trotzdem keine persönlichen Bankdaten lesen.'
   ]),
   ('Arbeit mit Kundendaten und KI-Assistenten', [
     'Der AI Assistant verwendet dieselben Customer Use Cases wie die reguläre Oberfläche. Die Language-Model-Ausgabe darf nur ein Tool anfordern. Vor dessen Ausführung prüft ToolPolicyGate die generelle Berechtigung, das Risiko, die erlaubten Argumente und die Aufruflimits.',
     'Für nicht verfügbare Daten wird eine verständliche, sichere Meldung angezeigt. Kreditfreigaben und andere schreibende Operationen sind nicht automatisch erlaubt. Die Demonstration arbeitet ausschließlich mit synthetischen Datensätzen, niemals mit echten Zugangsdaten.'
   ])
 ]),
 'credit/approval-guidelines.docx': ('Kreditfreigabe Richtlinie', [
   ('Genehmigungslimits und Vier-Augen-Prinzip', [
     'Die Kreditfreigabe ist im VirtualBank-Demo-Modell an Rollen, individuelle Freigabelimits und das Vier-Augen-Prinzip gebunden. Ein Kreditfreigeber mit 50.000 EUR Limit darf einen Antrag über 50.000 EUR nicht genehmigen. Die Währung muss bei Limits explizit geprüft werden.',
     'Die Person, die einen Antrag vorbereitet hat, darf denselben Antrag nicht im eigenen Namen freigeben. Die Statusübergänge dürfen nicht allein aufgrund einer Antwort des Language Models erfolgen. Eine vorherige menschliche Bestätigung und erneute serverseitige Prüfung sind erforderlich.'
   ]),
   ('AI-Agent und Audit', [
     'Das Agent-Tool approve_credit_application ist in dieser Referenz noch keine ausführbare Freigabefunktion. Der Assistant darf einen Genehmigungswunsch erkennen, muss die Ausführung jedoch bis zu einem geprüften Human-in-the-Loop-Protokoll ablehnen.',
     'Die Anwendung sollte eine Genehmigung als eigenständigen Command mit Idempotency-Key, Version, Berechtigungsprüfung und Audit-Eintrag modellieren. Die ausführende Person erhält vor der Bestätigung eine geprüfte Vorschau. Ein LLM kann Argumente vorschlagen, nicht autorisieren.'
   ])
 ]),
 'hr/salary-bands.docx': ('İnsan Kaynakları Maaş Bantları', [
   ('Sınıflandırma ve izinler', [
     'Bu belge VirtualBank için kurgusal insan kaynakları seviyelerini ve maaş bandı erişim politikasını açıklayan sentetik bir test dokümanıdır. Gerçek çalışan, ücret veya insan kaynakları kaydı içermez. Gizlilik sınıfı restricted olarak tanımlıdır.',
     'Sophie gibi HR uzmanı, yeterli clearance ile hr-specialist rolüne sahip olduğunda erişebilir. Platform yöneticisi Lena sistem ayarlarını yönetebilse bile doküman içeriğine otomatik erişim kazanmaz. Açık DENY kuralı bir ALLOW kuralından önce uygulanır.'
   ]),
   ('Belge arama ve denetim', [
     'Arama işlemi belge ACL verisini PostgreSQL tarafında değerlendirir. Yetkisiz chunk en yakın semantik sonuç olsa bile LLM bağlamına gönderilmez. Uygulama katmanındaki DocumentReadPolicy aynı kararı ikinci kez doğrular.',
     'Yönetici arayüzü ACL kurallarını değiştirebilir; ancak ACL yönetimi belge içeriğini okuma hakkı değildir. Testler sadece reddetmeyi değil, Sophie için bu belgeye pozitif erişimi de kontrol etmelidir.'
   ])
 ])
}


def generate_pdf():
    pdfmetrics.registerFont(TTFont('DejaVuVB', FONT))
    pdfmetrics.registerFont(TTFont('DejaVuVBBold', BOLD))
    heading = ParagraphStyle('h', fontName='DejaVuVBBold', fontSize=17, leading=24, textColor=colors.HexColor('#183B55'), spaceAfter=15)
    subhead = ParagraphStyle('sub', fontName='DejaVuVBBold', fontSize=12, leading=18, textColor=colors.HexColor('#27567A'), spaceBefore=12, spaceAfter=9)
    body = ParagraphStyle('body', fontName='DejaVuVB', fontSize=9.1, leading=15, alignment=TA_LEFT, spaceAfter=11)
    meta = ParagraphStyle('meta', fontName='DejaVuVB', fontSize=8, leading=12, textColor=colors.HexColor('#627788'), spaceAfter=20)
    for name, (title, sections) in pdf.items():
        path = ROOT / name
        doc = SimpleDocTemplate(str(path), pagesize=A4, rightMargin=50,leftMargin=50,topMargin=54,bottomMargin=48)
        story = [Paragraph(escape(title), heading), Paragraph('VirtualBank fictional engineering reference • Synthetic data only', meta)]
        for idx,(section, paragraphs) in enumerate(sections):
            if idx: story.append(PageBreak())
            story.append(Paragraph(escape(section), subhead))
            for text in paragraphs:
                story.append(Paragraph(escape(text), body))
        doc.build(story)
        print('PDF', name)


def generate_docx():
    for name, (title, sections) in docx.items():
        path = ROOT / name
        word = Document()
        sec = word.sections[0]; sec.top_margin = Inches(.82); sec.bottom_margin = Inches(.75)
        word.styles['Normal'].font.name = 'DejaVu Sans'; word.styles['Normal'].font.size = Pt(10)
        word.add_heading(title, 0)
        word.add_paragraph('VirtualBank fictional reference — exclusively synthetic content', style='Subtitle')
        for idx, (heading, paragraphs) in enumerate(sections):
            if idx: word.add_page_break()
            word.add_heading(heading, level=1)
            for text in paragraphs:
                word.add_paragraph(text)
        word.save(path)
        print('DOCX', name)


if __name__ == '__main__':
    if not (Path(FONT).is_file() and Path(BOLD).is_file()):
        raise SystemExit('DejaVu fonts not installed. Keep checked-in PDF/DOCX fixture binaries.')
    generate_pdf()
    generate_docx()
