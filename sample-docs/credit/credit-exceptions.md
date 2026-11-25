# Kredi İstisna Yönetimi

Kredi başvurusu istisnaları yetkili analist tarafından gözden geçirilir. 50.000 EUR üstü taleplerde uygun yetki limiti zorunludur. Kredi başvurusu K-100 örnek sentetik kayıt olarak kullanılır. AI asistan yalnızca izin verilen başvuru ve politika metinlerini birleştirebilir.

## İnceleme süreci

Kurgusal bir kredi istisnasında önce başvurunun atanmış analiste ait olup olmadığı kontrol edilir. İstisna gerekçesi, kredi tutarı, para birimi, hazırlayan kişi ve başvurunun mevcut durumu ayrı alanlar olarak ele alınır. Anlamlı olmayan veya eksik başvuru gerekçesi işlem yapılmasını haklı çıkarmaz.

## Yapay zekâ araçlarının sınırı

Asistan kredi başvurusunu okuyabilir ve ilgili belgeyi bulabilir, ancak kendi başına onay kararı veremez. `approve_credit_application` için rol kontrolü, tutar limiti, four-eyes şartı ve açık kullanıcı onayı gerekir. Model yalnızca bir talepte bulunabilir; veritabanındaki durumu doğrudan güncellemez.

## Veri erişimi ve denetim

Kredi analisti yalnızca kendisine atanmış başvuruları görebilir. Başvuruyla ilişkili müşterinin bilgileri ayrı müşteri modülü üzerinden ve yine yetki kontrolü uygulanarak getirilir. Satır düzeyindeki güvenlik, unutulan WHERE koşullarına karşı ikinci bir kontrol katmanı oluşturur.

## Örnek sorular

“K-100 başvurusunun durumu nedir?”, “Bu kredide hangi istisna politikası geçerlidir?”, “50.000 EUR üzerindeki bir başvuruyu kim onaylayabilir?” soruları birbirinden farklıdır: ilk soru yapılandırılmış kayda, diğerleri yetkili politika belgesine yönlendirilebilir. Belgede cevap yoksa asistan limit veya politika uydurmamalıdır.
