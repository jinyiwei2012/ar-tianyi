"""Generate the fixed LuoTianyi AR calibration marker and an A4 print sheet.

Install the two small generation-only dependencies before running:
    python -m pip install -r tools/requirements-marker.txt
    python tools/generate_qr_marker.py

The square PNG is the exact image imported into Unity.  The PDF places that
image at exactly 120 mm x 120 mm on A4; print it at 100% / Actual size.
"""

from pathlib import Path

import qrcode
from PIL import Image, ImageDraw
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[1]
MARKER_DIR = ROOT / "LuoTianyiAR" / "Assets" / "AR" / "Markers"
PDF_DIR = ROOT / "output" / "pdf"
PNG_PATH = MARKER_DIR / "LuoTianyiDeskMarkerV1.png"
PDF_PATH = PDF_DIR / "LuoTianyiDeskMarkerV1-A4-120mm.pdf"

PAYLOAD = "LuoTianyiAR|marker=desk-v1|size_mm=120"
CANVAS_SIZE = 2048
PHYSICAL_SIZE_MM = 120


def make_marker() -> Image.Image:
    qr = qrcode.QRCode(
        error_correction=qrcode.constants.ERROR_CORRECT_H,
        box_size=1,
        border=4,
    )
    qr.add_data(PAYLOAD)
    qr.make(fit=True)
    matrix = qr.get_matrix()

    image = Image.new("L", (CANVAS_SIZE, CANVAS_SIZE), 255)
    draw = ImageDraw.Draw(image)

    # A precise outer boundary makes the physical 120 mm extent unambiguous.
    draw.rectangle((24, 24, CANVAS_SIZE - 25, CANVAS_SIZE - 25), outline=0, width=16)

    modules = len(matrix)
    pixels_per_module = 27
    qr_size = modules * pixels_per_module
    qr_left = (CANVAS_SIZE - qr_size) // 2
    qr_top = (CANVAS_SIZE - qr_size) // 2
    for row, values in enumerate(matrix):
        for column, black in enumerate(values):
            if not black:
                continue
            x0 = qr_left + column * pixels_per_module
            y0 = qr_top + row * pixels_per_module
            draw.rectangle(
                (x0, y0, x0 + pixels_per_module - 1, y0 + pixels_per_module - 1),
                fill=0,
            )

    # Non-symmetric features stabilize orientation and make the top edge obvious.
    arrow_center = CANVAS_SIZE // 2
    draw.polygon(
        (
            (arrow_center, 70),
            (arrow_center - 105, 230),
            (arrow_center - 35, 230),
            (arrow_center - 35, 310),
            (arrow_center + 35, 310),
            (arrow_center + 35, 230),
            (arrow_center + 105, 230),
        ),
        fill=0,
    )

    # Three different corner signatures avoid accidental 90/180 degree symmetry.
    draw.ellipse((85, 90, 225, 230), fill=0)
    draw.ellipse((125, 130, 185, 190), fill=255)
    draw.rectangle((CANVAS_SIZE - 245, 90, CANVAS_SIZE - 85, 135), fill=0)
    draw.rectangle((CANVAS_SIZE - 245, 165, CANVAS_SIZE - 125, 210), fill=0)
    draw.rectangle((85, CANVAS_SIZE - 225, 135, CANVAS_SIZE - 85), fill=0)
    draw.rectangle((165, CANVAS_SIZE - 225, 245, CANVAS_SIZE - 175), fill=0)
    draw.polygon(
        (
            (CANVAS_SIZE - 245, CANVAS_SIZE - 90),
            (CANVAS_SIZE - 85, CANVAS_SIZE - 90),
            (CANVAS_SIZE - 85, CANVAS_SIZE - 250),
        ),
        fill=0,
    )

    return image


def make_print_sheet(marker_path: Path) -> None:
    pdf = canvas.Canvas(str(PDF_PATH), pagesize=A4)
    page_width, page_height = A4
    marker_size = PHYSICAL_SIZE_MM * mm
    x = (page_width - marker_size) / 2
    y = (page_height - marker_size) / 2

    pdf.setFont("Helvetica-Bold", 14)
    pdf.drawCentredString(page_width / 2, y + marker_size + 20 * mm, "LuoTianyi AR Calibration Marker V1")
    pdf.setFont("Helvetica", 10)
    pdf.drawCentredString(page_width / 2, y + marker_size + 13 * mm, "Print at 100% / Actual size. Do not use Fit to page.")
    pdf.drawCentredString(page_width / 2, y + marker_size + 8 * mm, "After printing, verify the outer square is exactly 120 mm x 120 mm.")
    pdf.drawImage(str(marker_path), x, y, marker_size, marker_size, preserveAspectRatio=True, mask="auto")

    # 20 mm ruler outside the tracked image provides a quick scale check.
    ruler_y = y - 15 * mm
    pdf.setLineWidth(0.5)
    pdf.line(x, ruler_y, x + 20 * mm, ruler_y)
    pdf.line(x, ruler_y - 2 * mm, x, ruler_y + 2 * mm)
    pdf.line(x + 20 * mm, ruler_y - 2 * mm, x + 20 * mm, ruler_y + 2 * mm)
    pdf.setFont("Helvetica", 9)
    pdf.drawCentredString(x + 10 * mm, ruler_y - 5 * mm, "20 mm check")
    pdf.setTitle("LuoTianyi AR Calibration Marker V1")
    pdf.save()


def main() -> None:
    MARKER_DIR.mkdir(parents=True, exist_ok=True)
    PDF_DIR.mkdir(parents=True, exist_ok=True)
    marker = make_marker()
    marker.save(PNG_PATH, format="PNG", dpi=(433.493, 433.493), optimize=True)
    make_print_sheet(PNG_PATH)
    print(PNG_PATH)
    print(PDF_PATH)


if __name__ == "__main__":
    main()
