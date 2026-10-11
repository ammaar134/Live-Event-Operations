# Fieldroom's visual direction

Revised 12 October 2026 after feedback that the first version's serif headlines,
italic slogans and small labels felt generic. The equipment illustrations worked;
the surrounding interface needed a more coherent identity and clearer hierarchy.

## References and decisions

These primary sources were read and their live sites inspected during the redesign.
They informed principles, rather than supplying components or artwork.

| Reference | Useful observation | Fieldroom decision |
| --- | --- | --- |
| [Teenage Engineering store](https://teenage.engineering/store) | Equipment can carry the character of a page when product images have room and labels are practical. | Keep the original illustrations prominent. Three illustrated category links lead into a functional catalog, with product names above availability details. |
| [Vitsœ](https://www.vitsoe.com/) | Consistent alignment, restrained hierarchy and direct explanations support a useful, everyday product. | Use rules, spacing and a desktop filter rail to organize the collection. Reduce decorative copy on account, loan and desk screens. |
| [Schibsted's typeface story](https://schibsted.com/the-schibsted-grotesk-story/) and [official font source](https://github.com/schibsted/schibsted-grotesk) | Henrik Kongsvoll / Bakken & Bæck developed a family for digital publishing with a recognisable voice across sizes and weights. | Use Schibsted Grotesk throughout. Vary weight, scale and spacing by role instead of pairing unrelated display and utility fonts. |

The forest ink, pale paper, citron return mark, original equipment still lifes and
equipment-library language form Fieldroom's own composition. The three category
panels are real filter links; there are no decorative actions or invented inventory
identifiers. Availability and physical copy labels retain their existing meaning.

## Type in context

One locally hosted variable font handles headings, paragraphs, forms and tables.
Large headings use a medium weight and tighter spacing; labels and body text keep
normal spacing. This makes the difference between a title and an instruction clear
without switching visual personalities.

| Role | Treatment |
| --- | --- |
| Opening headline | Fluid display size, approximately 40–80px, weight 580; tight spacing only at display sizes. |
| Page headings | Approximately 39–62px; short wording, normal sentence case. |
| Product names | 17–18px, medium weight; distinct from the smaller category/status metadata. |
| Body and booking controls | Primarily 14–16px with generous line height; forms have persistent labels. |
| References and asset identifiers | System monospace; tabular numerals where dates and quantities need alignment. |

The WOFF2 is preloaded and uses `font-display: swap`, so text stays readable while
it arrives. No remote font service, additional UI library or animation dependency
was introduced. [Font provenance and license](ASSETS.md).

## Layout and interaction

The desktop opening balances a large left-aligned statement with a short explanation,
then gives the illustrations a full-width shelf. Below, the browsing controls sit in
a side rail beside a three-column equipment grid. At tablet widths the filters move
above the grid; phones use two product columns and a compact three-category shelf.
The mobile product screen orders the illustration, booking controls and specification
details in the sequence a borrower needs them.

Forms use generous control heights, visible focus outlines and native date/time
inputs. Narrow booking forms stack pickup and return fields. Small image and arrow
transitions provide feedback; reduced-motion preferences disable transitions and
smooth scrolling. Color supports text labels rather than conveying status alone.

The same typography, spacing and controls extend to loan review, history, sign-in,
inventory and the lending desk. Existing booking rules and API behavior are unchanged.

## Review evidence

The browser suite captures [desktop](screenshots/catalog-desktop.png),
[phone catalog](screenshots/catalog-mobile.png), [phone product](screenshots/product-mobile.png)
and [lending desk](screenshots/lending-desk.png) views. It checks the bundled font,
horizontal overflow at 320, 390 and 768px, keyboard entry, illustrated category
filtering, search and date controls. Automated axe checks cover catalog, product
and desk screens. See [verification](VERIFICATION.md) for results and boundaries;
these checks do not constitute a complete accessibility audit.
