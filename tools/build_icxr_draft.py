from docx import Document
from docx.enum.section import WD_SECTION
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor


OUT = r"D:\GitRepository\OSP2\OSP-Archive\docs\ICXR2026_主动重置论文中文工作稿.docx"
RESULT_DIR = r"D:\zsm\Desktop\OSP数据处理\OSP_APF_10_P4\multigroup_user_scaling_figures"


def set_cell_shading(cell, fill):
    tc_pr = cell._tc.get_or_add_tcPr()
    shd = tc_pr.find(qn("w:shd"))
    if shd is None:
        shd = OxmlElement("w:shd")
        tc_pr.append(shd)
    shd.set(qn("w:fill"), fill)


def set_cell_width(cell, width_cm):
    tc_pr = cell._tc.get_or_add_tcPr()
    tc_w = tc_pr.find(qn("w:tcW"))
    if tc_w is None:
        tc_w = OxmlElement("w:tcW")
        tc_pr.append(tc_w)
    tc_w.set(qn("w:w"), str(int(width_cm * 567)))
    tc_w.set(qn("w:type"), "dxa")


def set_cell_margins(table, top=80, start=120, bottom=80, end=120):
    tbl_pr = table._tbl.tblPr
    tbl_cell_mar = tbl_pr.find(qn("w:tblCellMar"))
    if tbl_cell_mar is None:
        tbl_cell_mar = OxmlElement("w:tblCellMar")
        tbl_pr.append(tbl_cell_mar)
    for m, v in (("top", top), ("start", start), ("bottom", bottom), ("end", end)):
        node = tbl_cell_mar.find(qn(f"w:{m}"))
        if node is None:
            node = OxmlElement(f"w:{m}")
            tbl_cell_mar.append(node)
        node.set(qn("w:w"), str(v))
        node.set(qn("w:type"), "dxa")


def set_table_borders(table, color="BFBFBF", sz="4"):
    tbl_pr = table._tbl.tblPr
    borders = tbl_pr.find(qn("w:tblBorders"))
    if borders is None:
        borders = OxmlElement("w:tblBorders")
        tbl_pr.append(borders)
    for edge in ("top", "left", "bottom", "right", "insideH", "insideV"):
        tag = f"w:{edge}"
        element = borders.find(qn(tag))
        if element is None:
            element = OxmlElement(tag)
            borders.append(element)
        element.set(qn("w:val"), "single")
        element.set(qn("w:sz"), sz)
        element.set(qn("w:space"), "0")
        element.set(qn("w:color"), color)


def set_repeat_table_header(row):
    tr_pr = row._tr.get_or_add_trPr()
    tbl_header = OxmlElement("w:tblHeader")
    tbl_header.set(qn("w:val"), "true")
    tr_pr.append(tbl_header)


def set_run(run, font="Times New Roman", size=10.5, bold=False, italic=False):
    run.font.name = font
    run._element.rPr.rFonts.set(qn("w:eastAsia"), "SimSun")
    run.font.size = Pt(size)
    run.bold = bold
    run.italic = italic


def add_para(doc, text="", style=None, align=None):
    p = doc.add_paragraph(style=style)
    if align is not None:
        p.alignment = align
    if text:
        r = p.add_run(text)
        set_run(r)
    return p


def add_heading(doc, text, level):
    p = doc.add_paragraph(style=f"Heading {level}")
    r = p.add_run(text)
    set_run(r, size=13 if level == 1 else 11.5, bold=True)
    return p


def add_bullet(doc, text):
    p = doc.add_paragraph(style="List Bullet")
    r = p.add_run(text)
    set_run(r)
    return p


def add_numbered(doc, text):
    p = doc.add_paragraph(style="List Number")
    r = p.add_run(text)
    set_run(r)
    return p


def add_table(doc, headers, rows, widths):
    table = doc.add_table(rows=1, cols=len(headers))
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    table.autofit = False
    set_table_borders(table)
    set_cell_margins(table)
    hdr = table.rows[0]
    set_repeat_table_header(hdr)
    for i, h in enumerate(headers):
        cell = hdr.cells[i]
        set_cell_width(cell, widths[i])
        set_cell_shading(cell, "E8EEF5")
        cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
        p = cell.paragraphs[0]
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        r = p.add_run(h)
        set_run(r, size=9.5, bold=True)
    for row in rows:
        cells = table.add_row().cells
        for i, value in enumerate(row):
            cell = cells[i]
            set_cell_width(cell, widths[i])
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            p = cell.paragraphs[0]
            p.alignment = WD_ALIGN_PARAGRAPH.CENTER if i == 0 else WD_ALIGN_PARAGRAPH.LEFT
            r = p.add_run(value)
            set_run(r, size=9.5)
    return table


def add_figure(doc, image_path, caption):
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p.add_run()
    run.add_picture(image_path, width=Cm(15.4))
    cap = doc.add_paragraph()
    cap.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = cap.add_run(caption)
    set_run(r, size=9.5, italic=True)
    cap.paragraph_format.space_after = Pt(6)


def style_document(doc):
    section = doc.sections[0]
    section.page_width = Cm(21.0)
    section.page_height = Cm(29.7)
    section.top_margin = Cm(2.5)
    section.bottom_margin = Cm(2.5)
    section.left_margin = Cm(2.6)
    section.right_margin = Cm(2.6)
    section.header_distance = Cm(1.25)
    section.footer_distance = Cm(1.25)

    styles = doc.styles
    normal = styles["Normal"]
    normal.font.name = "Times New Roman"
    normal._element.rPr.rFonts.set(qn("w:eastAsia"), "SimSun")
    normal.font.size = Pt(10.5)
    normal.paragraph_format.line_spacing = 1.15
    normal.paragraph_format.space_after = Pt(4)

    for name, size, before, after in (
        ("Heading 1", 13, 12, 6),
        ("Heading 2", 11.5, 8, 4),
        ("Heading 3", 10.5, 6, 3),
    ):
        style = styles[name]
        style.font.name = "Times New Roman"
        style._element.rPr.rFonts.set(qn("w:eastAsia"), "SimSun")
        style.font.size = Pt(size)
        style.font.bold = True
        style.font.color.rgb = RGBColor(0, 0, 0)
        style.paragraph_format.space_before = Pt(before)
        style.paragraph_format.space_after = Pt(after)
        style.paragraph_format.keep_with_next = True

    for name in ("List Bullet", "List Number"):
        style = styles[name]
        style.font.name = "Times New Roman"
        style._element.rPr.rFonts.set(qn("w:eastAsia"), "SimSun")
        style.font.size = Pt(10.5)
        style.paragraph_format.space_after = Pt(3)


def add_front_matter(doc):
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("面向局部冲突恢复的多人重定向行走主动重置仲裁方法")
    set_run(r, size=16, bold=True)

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("中文工作稿，按 ICXR 2026 / Springer LNCS 投稿结构组织")
    set_run(r, size=10, italic=True)

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("作者信息按双盲要求暂不填写")
    set_run(r, size=10)

    add_heading(doc, "摘要", 1)
    add_para(
        doc,
        "多人重定向行走需要在共享物理空间中同时保证用户安全和行走连续性。现有用户间重置通常在距离风险接近安全阈值时被动触发，能够避免即时碰撞，但不显式评估冲突处理后的恢复质量。本文关注一类局部恢复问题：一次用户间冲突可能引发双边用户重置、短距离连续重置，或很短的冲突后连续行走距离。为降低这类高代价局部恢复事件，本文提出一种面向局部冲突恢复的单侧主动重置仲裁方法。该方法在用户间冲突仍存在恢复余量、但恢复余量持续下降时进入主动干预窗口，并通过局部 max-min 仲裁选择更适合执行重置的一侧用户。本文不引入新的复杂重置方向策略，而沿用原有方向策略，以便单独检验触发时机和用户选择对恢复质量的影响。在 3、4、5、6 用户 APF_OSP 实验中，Active Reset 没有增加总 reset rows，并在所有用户数下提高 D_recover median。4、5、6 用户下，short <2m share 和 next reset <2m share 均显著下降。结果表明，主动重置主要价值在于替代部分高代价被动恢复，而不是增加额外打断。"
    )
    add_para(doc, "关键词：重定向行走；多人虚拟现实；用户间冲突；主动重置；局部恢复；行走连续性")

    add_heading(doc, "Abstract", 1)
    add_para(
        doc,
        "Multi-user redirected walking must preserve physical safety while maintaining continuous walking in a shared tracked space. Existing user-user reset mechanisms are typically triggered passively when the distance risk approaches a safety threshold. They can prevent immediate physical collisions, but they do not explicitly optimize recovery quality after a local conflict. This paper focuses on a local recovery problem in which one user-user conflict may lead to bilateral user resets, short-distance consecutive resets, or a short walking distance before the next reset. To reduce such high-cost local recovery events, we propose a unilateral proactive reset arbitration method for local conflict recovery. The method enters a proactive intervention window when a user pair still has positive recovery margin but the margin is decreasing, and then uses a local max-min arbitration rule to select which user should reset. The reset direction follows the existing policy, allowing trigger timing and user selection to be evaluated separately. In APF_OSP experiments with 3, 4, 5, and 6 users, Active Reset did not increase total reset rows and improved the median D_recover for all user counts. For 4, 5, and 6 users, both the short <2m share and the next-reset <2m share decreased significantly. These results indicate that proactive reset can replace part of the high-cost passive recovery process rather than simply adding extra interruptions."
    )
    add_para(doc, "Keywords: Redirected walking; Multi-user virtual reality; User-user conflict; Proactive reset; Local recovery; Walking continuity")


def add_introduction(doc):
    add_heading(doc, "1 引言", 1)
    for text in [
        "重定向行走通过对虚拟运动和真实运动之间的映射进行细微调节，使用户在有限物理空间中感知到更大的虚拟空间 [1,2]。多人场景进一步提高了问题难度：多个用户共享同一个物理空间，每个用户的真实位置、运动方向和重置状态都会影响其他用户的安全余量。",
        "在单用户场景中，常见风险主要来自用户与物理边界或障碍物的关系。多人场景中的用户间冲突不同。它由两个动态用户共同产生，冲突是否会扩大不仅取决于当前距离，还取决于相对运动趋势、预测轨迹以及重置后的恢复空间。两个用户即使暂时距离较近，只要正在自然分离，也未必需要干预；反之，距离尚未到达危险阈值但相对速度较大时，冲突可能已经进入需要关注的窗口。",
        "现有重置机制通常以即时安全为目标。当用户接近物理边界、障碍物或其他用户时，系统触发显式重置以避免真实碰撞 [3,5]。该机制是必要的，但它主要回答“现在是否必须避免碰撞”，而没有回答“这次处理后，局部冲突能否稳定恢复”。因此，一次局部用户间冲突可能引发双边用户重置、短距离连续重置，或很短的冲突后连续行走距离。本文将这些现象视为高代价局部恢复的客观代理指标。",
        "本文提出一种面向局部冲突恢复的主动重置仲裁方法。方法的核心不是简单提前触发重置，而是在局部冲突仍存在恢复余量、但恢复余量持续下降时进入主动干预窗口。随后，方法通过局部 max-min 仲裁选择单侧用户执行主动重置，目标是在只打断一名用户的条件下改善当前 pair 的较差恢复结果。本文沿用原有重置方向策略，不把方向优化作为贡献，以便更清楚地检验触发时机和用户选择本身的作用。",
    ]:
        add_para(doc, text)

    add_para(doc, "本文的贡献有三点。第一，本文将多人 RDW 中用户间局部冲突的恢复质量作为独立问题，并将高代价恢复具体化为双边用户重置、短距离连续重置和低 D_recover。第二，本文提出一种基于恢复余量趋势的单侧主动重置仲裁方法，在冲突尚未越过不可恢复边界时触发候选干预，并通过局部 max-min 规则选择重置用户。第三，本文用 3 到 6 用户的 APF_OSP 实验验证该方法，结果显示主动重置没有增加总体 reset rows，并在中高密度用户场景下稳定降低短距离恢复风险。")


def add_related_work(doc):
    add_heading(doc, "2 相关工作", 1)
    add_heading(doc, "2.1 重定向行走中的重置代价", 2)
    add_para(doc, "Razzaque 等最早系统提出 redirected walking 的基本思想，即通过操纵用户在真实空间和虚拟空间中的运动映射，使用户在有限物理空间中探索更大的虚拟环境 [1]。Steinicke 等进一步测量了平移、旋转和曲率增益的感知阈值，为“尽量不被察觉的重定向”提供了心理物理基础 [2]。Liu 等近期综述了 RDW 的控制器、多人协同、动态环境和多感官扩展等方向，说明该领域已经从单用户边界规避扩展到复杂共享空间中的系统性控制问题 [10]。")
    add_para(doc, "当隐式重定向不足以维持安全时，系统需要显式重置。Williams 等比较了 Freeze-Backup、Freeze-Turn 和 2:1-Turn 等重置方式，说明重置是有限物理空间中继续行走的重要安全手段 [3]。Hodgson 等在受限虚拟世界中比较不同重定向算法，使用 potential wall contacts、reset count 和行走距离等指标评价 RDW 性能 [4]。这些工作说明，重置数量和两次重置之间的距离已经是 RDW 评价中的核心指标。本文在此基础上进一步关注用户间冲突后的局部恢复质量。")
    add_heading(doc, "2.2 多人 RDW 中的用户间冲突", 2)
    add_para(doc, "多人 RDW 不仅要处理用户与边界的关系，还要处理用户之间的动态接近。Bachmann 等将人工势场用于多人 RDW 和重置，把障碍物和其他用户建模为排斥源，证明 APF 方法可以支持多用户场景并减少重置 [5]。Thomas 和 Rosenberg 也提出基于人工势函数的通用 reactive RDW 算法，为 APF 类重定向提供了更一般的形式 [6]。Dong 等的 FREE-RDW 利用非前向步态的感知阈值，展示了多人碰撞规避可以从运动形式和可察觉性两个层面同时设计 [11]。")
    add_para(doc, "另一类方法通过空间分配或未来轨迹规划降低冲突。Jeon 等提出动态最优空间划分 OSP，在共享物理空间中为用户分配子空间，以降低用户碰撞风险和总重置次数 [7]。Xu 等提出 Optimal Pose Guided 方法，通过离散化物理空间中的位置和朝向，并为标准 pose 计算长期安全分数，引导用户走向更安全的未来 pose [12]。后续工作进一步把 POI 位置、历史行走数据和安全区域等因素加入 pose 或路径规划 [13-15]。这些方法说明，未来状态和空间布局对 RDW 控制非常重要。")
    add_para(doc, "预测和学习方法也推动了多用户 RDW。Hirt 等将预测思想引入多用户 APF RDW，强调在多用户共址环境中结合轨迹预测和势场重定向 [8]。Lee 等提出多用户 reset controller，用强化学习同时考虑障碍物和多用户运动，以优化 reset 方向并减少平均 reset 数量 [9]。这些方法都重视避碰和 reset 优化，但主要优化全局 reset 数量、空间分配或 reset 方向。本文关注的不是新的方向优化，而是一次用户间冲突是否会在恢复阶段演化为双边重置或短距离连续重置。")
    add_heading(doc, "2.3 本文定位", 2)
    add_para(doc, "本文不试图替代已有 RDW 控制器，也不提出新的复杂重置方向策略。本文关注一个更窄的问题：在方向策略保持一致的前提下，仅改变主动干预窗口和单侧用户选择，是否能够减少双边重置和短距离连续重置。这个定位有助于避免把收益混入方向优化或全局空间分配。")


def add_problem(doc):
    add_heading(doc, "3 问题定义", 1)
    add_heading(doc, "3.1 局部用户间冲突", 2)
    add_para(doc, "本文将局部用户间冲突定义为：在一段局部时间窗口内，两个用户的物理距离、相对运动趋势或预测轨迹显示其可能进入安全风险区域，并可能触发用户间重置的 pair-level 状态。")
    add_para(doc, "该定义强调三个条件。第一，距离必须接近安全相关区域；第二，相对运动趋势必须表明冲突可能继续发展；第三，该状态必须可能影响后续重置或连续行走距离。")
    add_heading(doc, "3.2 冲突恶化与高代价恢复", 2)
    add_para(doc, "冲突恶化不是主观判断，而是一个可观测过程。本文将其定义为：一个用户 pair 从仍可通过单侧低代价干预恢复的状态，发展为需要被动用户间重置、双边用户重置或短距离连续重置的状态。")
    add_para(doc, "高代价恢复指一次局部用户间冲突导致更多用户被打断、更短距离内再次重置，或重置后可连续行走距离更短。该定义延续了 RDW 文献中用 reset count 和 distance between resets 衡量重置代价的思路 [3,4]，但把评价单位从全局 episode 进一步收缩到用户间冲突片段。本文重点使用三个代理指标：双边用户重置、短距离连续重置和 D_recover。")
    add_table(
        doc,
        ["概念", "可观测定义", "为什么重要"],
        [
            ["双边用户重置", "同一局部冲突导致两名用户均发生 user reset", "说明一次 pair-level 冲突打断了更多用户"],
            ["短距离连续重置", "一次 reset 后在 1m、2m 或 3m 内再次 reset", "说明前一次恢复没有带来稳定行走空间"],
            ["D_recover", "冲突处理后到下一次 reset 的连续行走距离", "直接度量恢复后的行走连续性"],
        ],
        [3.2, 5.0, 6.0],
    )
    add_heading(doc, "3.3 研究问题", 2)
    for text in [
        "RQ1：被动用户间重置下，是否存在可观测的高代价局部恢复现象？",
        "RQ2：主动重置是否能够替代更差的后续恢复，而不是额外增加打断？",
        "RQ3：局部 max-min 仲裁是否比简单选人策略更符合恢复目标？",
    ]:
        add_numbered(doc, text)


def add_method(doc):
    add_heading(doc, "4 方法", 1)
    add_heading(doc, "4.1 方法概述", 2)
    add_para(doc, "本文方法包含两个核心决策：是否进入主动干预窗口，以及选择哪一侧用户执行主动重置。reset 朝向沿用已有策略，不作为本文创新点。这一区分很重要，因为已有工作已经从 APF、空间划分、预测和学习等角度优化了重定向或 reset 方向 [5-9]；本文要单独检验的是触发时机和单侧仲裁。")
    add_heading(doc, "4.2 主动干预窗口", 2)
    add_para(doc, "现有 recoverability 判定若以 best.Margin < 0 作为触发条件，语义上更接近“预测到已经不可恢复”。为了支撑主动恢复的论文目标，本文引入恢复余量趋势判定：只有当 pair 仍有正恢复余量，但恢复余量已进入保守窗口并持续下降时，才进入主动干预候选。")
    add_para(doc, "设 best.Margin 表示预测窗口内最优左右转组合下的最小安全余量。主动窗口满足：0 < best.Margin <= clamp(closingSpeed * 0.30s + 0.05m, 0.15m, 0.50m)，且最近 5 帧中至少 3 帧 margin 下降。该阈值是保守估计：0.30s 表示干预生效提前量，0.05m 表示预测不确定性缓冲，上限 0.50m 用于抑制过早重置。")
    add_para(doc, "该判定与已有的不可恢复判定不同。若直接以 best.Margin < 0 作为触发条件，系统已经预测到最优局部转向组合仍会越过安全边界。此时主动重置更接近预测版被动安全处理。本文的恢复余量趋势判定要求 best.Margin 仍为正，因此触发点位于“无须干预”和“被动安全 reset”之间。")
    add_heading(doc, "4.3 单侧用户仲裁", 2)
    add_para(doc, "进入主动窗口后，系统分别估计重置用户 A 或用户 B 后的局部恢复结果。仲裁目标不是选择当前最危险用户，而是选择能改善当前 pair 较差恢复状态的一侧用户。本文采用局部 max-min 思路：比较两种单侧重置方案下 pair 中较差一侧的预计可行走距离，并选择较大者。")
    add_para(doc, "为避免主动重置成为额外打断，本文加入最小预期收益门槛。仅当被选方案的 worst remaining distance 至少比保持当前状态多 0.20m 时，才接受主动重置候选。否则该候选被拒绝，原因记为 InsufficientExpectedImprovement。")
    add_heading(doc, "4.4 算法流程", 2)
    for text in [
        "构建相邻用户 pair，并过滤掉静止、非相向或单侧运动导致的候选。",
        "计算预测窗口内的恢复余量 best.Margin、当前 closing speed 和 margin 下降趋势。",
        "当恢复余量为正、进入主动窗口且趋势持续恶化时，创建主动触发事件。",
        "对 pair 两侧用户分别估计单侧重置后的恢复结果，并通过 max-min 规则选择用户。",
        "若预计收益未超过最小门槛，拒绝该候选；否则进入冷却和安全检查，并生成主动重置意图。",
    ]:
        add_numbered(doc, text)


def add_experiment(doc):
    add_heading(doc, "5 实验设计", 1)
    add_heading(doc, "5.1 实验目标", 2)
    add_para(doc, "实验目标不是证明主动重置一定减少所有 reset，而是检验它是否减少高代价局部恢复事件，并且没有用大量额外主动 reset 换取表面改善。")
    add_para(doc, "RDW 研究常使用仿真作为初步评价手段，因为仿真可以在相同路径、相同环境和相同参数下反复比较多种控制器 [16]。这种设置不能替代真实用户实验，但适合评估 reset 数量、恢复距离和不同策略之间的相对差异。本文沿用这一思路，在 APF_OSP 多用户仿真中比较 Baseline 与 Active Reset。")
    add_heading(doc, "5.2 对比方法", 2)
    add_table(
        doc,
        ["方法", "触发逻辑", "用途"],
        [
            ["Passive baseline", "仅使用原有被动用户间 reset", "验证高代价局部恢复是否存在"],
            ["Recoverability trigger", "best.Margin < 0 连续确认后触发", "对比预测不可恢复触发"],
            ["RecoveryMarginTrend", "best.Margin > 0 且进入下降窗口时触发", "检验恢复余量趋势触发是否更早且更有效"],
            ["Ablation: simple selection", "触发窗口相同，但用简单选人策略", "验证 max-min 仲裁是否必要"],
        ],
        [3.3, 5.4, 4.8],
    )
    add_heading(doc, "5.3 评价指标", 2)
    for text in [
        "total reset count：检查主动重置是否只是粗暴增加打断。",
        "proactive user reset count：度量主动干预规模，过高说明门控太激进。",
        "passive user reset count：检查主动重置是否替代了部分被动用户间 reset。",
        "bilateral user reset count / ratio：度量一次 pair-level 冲突是否打断两名用户。",
        "short-distance reset ratio：度量 reset 后是否在短距离内再次打断。",
        "D_recover：度量冲突处理后到下一次 reset 的连续行走距离，需报告均值、中位数和低分位数。",
    ]:
        add_bullet(doc, text)
    add_heading(doc, "5.4 场景设置", 2)
    add_para(doc, "实验覆盖 3、4、5、6 个用户。用户数越多，用户间相对接近和局部竞争窗口越多，因此该设置可用于观察主动重置在不同冲突压力下的稳定性。每个用户数下均比较 Baseline 与 Active Reset 条件。")
    add_para(doc, "当前数据来自 APF_OSP 多用户实验处理结果。所有图表均由分析脚本从派生 CSV 读取，不手工填数。总 reset 组成直接统计 normalized_reset_rows.csv；恢复距离统计来自 event_recovery_outcomes.csv；episode-level 显著性来自每组 100 个 episode 的 permutation test。")
    add_heading(doc, "5.5 统计与报告方式", 2)
    add_para(doc, "结果不应只报告平均值。双边 reset 和短距离 reset 应报告计数和比例；D_recover 应报告分布、低分位数和 D_recover < 1m / 2m / 3m 的比例。由于 event 数会随用户数增加而自然增长，跨用户数比较不能只看绝对 event 数，而应重点比较同一用户数下 Baseline 与 Active Reset 的差异。")
    add_para(doc, "episode-level 统计使用两侧 independent permutation test。显著性只作为辅助证据，正文优先报告 effect size 和方向。对于 3 用户场景中未显著的短距离比例，本文只表述为下降趋势，不写成稳定显著改善。")


def add_results_placeholders(doc):
    add_heading(doc, "6 结果与分析", 1)
    add_para(doc, "本节使用 3、4、5、6 用户 APF_OSP 实验的处理结果。每个用户数下比较 Baseline 与 Active Reset 条件。episode-level 显著性检验以 episode 为独立样本，每组 100 个 episode，p 值来自两侧 independent permutation test。")

    add_heading(doc, "6.1 Active Reset 没有增加总体 reset rows", 2)
    add_para(doc, "首先检查 Active Reset 是否只是额外插入更多 reset。结果显示，3、4、5、6 用户下，Active Reset 的 total reset rows 均低于 Baseline，分别为 7473 -> 7347、11758 -> 11341、17318 -> 16384、23870 -> 22966。ordinary USER_RESET 也明显下降，说明主动重置更像是替代了一部分被动用户间 reset，而不是简单增加额外打断。")
    add_table(
        doc,
        ["用户数", "条件", "Total rows", "Wall", "Ordinary USER", "Proactive", "USER+PRO", "Mean distance"],
        [
            ["3", "Baseline", "7473", "6154", "1319", "0", "1319", "8.005 m"],
            ["3", "Active", "7347", "6154", "878", "315", "1193", "8.168 m"],
            ["4", "Baseline", "11758", "8612", "3146", "0", "3146", "6.871 m"],
            ["4", "Active", "11341", "8514", "2046", "781", "2827", "7.126 m"],
            ["5", "Baseline", "17318", "11271", "6047", "0", "6047", "5.911 m"],
            ["5", "Active", "16384", "11175", "3774", "1435", "5209", "6.222 m"],
            ["6", "Baseline", "23870", "14122", "9748", "0", "9748", "5.187 m"],
            ["6", "Active", "22966", "13893", "6699", "2374", "9073", "5.384 m"],
        ],
        [1.2, 2.1, 2.0, 1.6, 2.1, 1.8, 1.8, 2.2],
    )
    add_figure(
        doc,
        RESULT_DIR + r"\fig0_total_reset_rows_composition.png",
        "图 1. 3/4/5/6 用户下 Baseline 与 Active Reset 的 reset rows 组成。Active Reset 没有增加总体 reset rows。"
    )

    add_heading(doc, "6.2 Active Reset 提高用户相关事件后的恢复距离", 2)
    add_para(doc, "随后观察用户相关事件后到下一次相关 reset/event 的恢复行走距离。Active Reset 在所有用户数下均提高了 median recovery distance。3 用户下中位数从 3.262 m 提高到 3.614 m；4 用户下从 2.308 m 提高到 3.329 m；5 用户下从 2.120 m 提高到 2.789 m；6 用户下从 1.676 m 提高到 2.199 m。")
    add_para(doc, "短距离恢复比例也下降。以 2m 为阈值，Baseline 与 Active Reset 的 <2m 比例分别为：3 用户 32.22% -> 28.61%，4 用户 45.15% -> 34.64%，5 用户 48.18% -> 39.69%，6 用户 55.56% -> 46.92%。这说明 Active Reset 主要改善的是冲突后的短距离恢复失败风险。")
    add_table(
        doc,
        ["用户数", "Baseline median", "Active median", "Baseline <2m", "Active <2m"],
        [
            ["3", "3.262 m", "3.614 m", "32.22%", "28.61%"],
            ["4", "2.308 m", "3.329 m", "45.15%", "34.64%"],
            ["5", "2.120 m", "2.789 m", "48.18%", "39.69%"],
            ["6", "1.676 m", "2.199 m", "55.56%", "46.92%"],
        ],
        [2.0, 3.1, 3.1, 3.1, 3.1],
    )
    add_figure(
        doc,
        RESULT_DIR + r"\fig7_user_collision_recovery_distance_survival.png",
        "图 2. 用户相关事件后的恢复距离 survival curve。Active Reset 曲线整体高于 Baseline，表示更高比例的事件可获得较长连续行走距离。"
    )

    add_heading(doc, "6.3 短距离恢复风险在 4/5/6 用户下显著下降", 2)
    add_para(doc, "episode-level 检验表明，D_recover median 在所有用户数下均显著提升。提升量分别为 0.615 m、0.903 m、0.690 m 和 0.568 m。短距离风险指标在 4、5、6 用户下稳定显著下降；3 用户下两个 <2m share 指标下降方向一致，但未达到显著。")
    add_table(
        doc,
        ["用户数", "D_recover median", "Short <2m share", "Next reset <2m share"],
        [
            ["3", "+0.615 m, p=0.0050", "-2.86 pp, p=0.2976", "-2.92 pp, p=0.2861"],
            ["4", "+0.903 m, p=0.0001", "-11.88 pp, p=0.0001", "-10.61 pp, p=0.0001"],
            ["5", "+0.690 m, p=0.0001", "-9.22 pp, p=0.0001", "-8.93 pp, p=0.0001"],
            ["6", "+0.568 m, p=0.0001", "-10.32 pp, p=0.0001", "-8.89 pp, p=0.0001"],
        ],
        [1.6, 4.4, 4.4, 4.4],
    )
    add_figure(
        doc,
        RESULT_DIR + r"\fig6_episode_violin_significance.png",
        "图 3. episode-level 分布和 permutation test 显著性。D_recover median 在所有用户数下提升；4/5/6 用户下短距离风险下降更稳定。"
    )

    add_heading(doc, "6.4 方法收益在中高密度场景下更稳定", 2)
    add_para(doc, "随着用户数从 3 增加到 6，Baseline 的恢复距离下降，短距离恢复比例上升，说明多人密度增加会加重局部恢复问题。Active Reset 在 4、5、6 用户下的收益更稳定：D_recover median 均显著提高，short <2m share 和 next reset <2m share 均显著下降。3 用户场景下，冲突压力较低，主动重置仍显著提升 D_recover median，但短距离比例下降不显著。")

    add_heading(doc, "6.5 结果解释", 2)
    add_para(doc, "这些结果支持本文的核心假设：主动重置的价值不在于增加 reset 数量，而在于把一部分高代价的被动用户间恢复转化为更可控的单侧干预。全局 reset rows 没有增加，ordinary USER_RESET 下降，且用户相关事件后的恢复距离分布右移，说明 Active Reset 没有简单地用更多打断换取指标改善。")
    add_para(doc, "同时，结果也说明方法有适用边界。轻载 3 用户场景下，短距离风险指标下降不显著，说明当自然冲突较少时，主动干预的可替代空间有限。方法更适合中高冲突密度场景，即被动恢复更容易产生短距离连续 reset 的情况。")


def add_discussion_conclusion(doc):
    add_heading(doc, "7 讨论", 1)
    add_para(doc, "本文方法的关键边界是：它不优化 reset 方向，也不解决所有全局空间分配问题。它只回答一个更窄的问题：当局部用户间冲突仍有恢复余量但正在恶化时，是否可以通过一次单侧主动 reset 减少后续高代价恢复。这个边界使本文区别于 OSP 的空间划分思路 [7]、预测 APF 重定向 [8] 和学习型多用户 reset controller [9]。")
    add_para(doc, "这个边界有两个好处。第一，因果关系更清楚，实验结果主要反映触发窗口和用户仲裁的作用。第二，方法更容易作为模块接入现有多人 RDW 系统，而不要求重写控制器或重置方向策略。")
    add_para(doc, "方法的主要风险是误触发。如果主动窗口过早，系统会打断原本可能自然化解的冲突。因此阈值设计采用保守策略，并通过最小预期收益门槛过滤无效候选。")
    add_para(doc, "本实验仍有局限。当前结果来自仿真日志和 APF_OSP 设置，尚未包含真实用户主观体验或不同 RDW 控制器下的泛化验证。因此，本文只能声称主动重置改善了客观恢复指标，不能直接声称其提升了主观沉浸感。若后续加入真实参与者实验，需要按 ICXR 要求报告伦理审批、参与者人口统计信息和实验协议。")
    add_heading(doc, "8 结论", 1)
    add_para(doc, "本文提出一种面向局部冲突恢复的多人 RDW 主动重置仲裁方法。该方法将用户间冲突的高代价恢复具体化为双边用户重置、短距离连续重置和低 D_recover，并在仍有恢复余量但趋势恶化的窗口内选择单侧用户执行主动 reset。本文不引入新的重置方向优化，而聚焦触发时机和用户选择。后续实验将验证该方法是否能够减少高代价局部恢复事件，并明确其适用边界。")
    add_heading(doc, "参考文献", 1)
    references = [
        "[1] Razzaque, S., Kohn, Z., Whitton, M. C. Redirected walking. In: Eurographics 2001 Short Presentations, 2001.",
        "[2] Steinicke, F., Bruder, G., Jerald, J., Frenz, H., Lappe, M. Estimation of detection thresholds for redirected walking techniques. IEEE Transactions on Visualization and Computer Graphics, 16(1), 17-27, 2010. https://doi.org/10.1109/TVCG.2009.62",
        "[3] Williams, B., Narasimham, G., Rump, B., McNamara, T. P., Carr, T. H., Rieser, J. J., Bodenheimer, B. Exploring large virtual environments with an HMD when physical space is limited. In: Proceedings of APGV 2007, pp. 41-48, 2007. https://doi.org/10.1145/1272582.1272590",
        "[4] Hodgson, E., Bachmann, E. R., Thrash, T. Performance of redirected walking algorithms in a constrained virtual world. IEEE Transactions on Visualization and Computer Graphics, 20(4), 579-587, 2014. https://doi.org/10.1109/TVCG.2014.34",
        "[5] Bachmann, E. R., Hodgson, E., Hoffbauer, C., Messinger, J. Multi-user redirected walking and resetting using artificial potential fields. IEEE Transactions on Visualization and Computer Graphics, 25(5), 2022-2031, 2019. https://doi.org/10.1109/TVCG.2019.2898764",
        "[6] Thomas, J., Rosenberg, E. S. A general reactive algorithm for redirected walking using artificial potential functions. In: Proceedings of IEEE VR 2019, pp. 56-62, 2019. https://doi.org/10.1109/VR.2019.8797983",
        "[7] Jeon, S. B., Kwon, S. U., Hwang, J. Y., Cho, Y. H., Kim, H., Park, J., Lee, I. K. Dynamic optimal space partitioning for redirected walking in multi-user environment. ACM Transactions on Graphics, 41(4), Article 90, 2022. https://doi.org/10.1145/3528223.3530113",
        "[8] Hirt, C., Isaak, N., Holz, C., Kunz, A. Predictive multiuser redirected walking using artificial potential fields. Frontiers in Virtual Reality, 5, Article 1259429, 2024. https://doi.org/10.3389/frvir.2024.1259429",
        "[9] Lee, H. J., Jeon, S.-B., Cho, Y.-H., Lee, I.-K. Multi-user reset controller for redirected walking using reinforcement learning. arXiv:2306.11433, 2023.",
        "[10] Liu, J.-H., Ren, Y.-F., Gan, Q. W., Huang, K., Chen, F. X. Y., Luo, E.-X., Tang, K. Y., Fu, Y.-Y., Fan, C.-W., Zhang, F.-L., Zhang, S.-H. A survey on redirected walking in virtual reality. IEEE Transactions on Visualization and Computer Graphics, 2024.",
        "[11] Dong, T., Gao, T., Dong, Y., Wang, L., Hu, K., Fan, J. FREE-RDW: A multi-user redirected walking method for supporting non-forward steps. IEEE Transactions on Visualization and Computer Graphics, 2024.",
        "[12] Xu, S.-Z., Lv, T., He, G., Chen, C.-H., Zhang, F.-L., Zhang, S.-H. Optimal pose guided redirected walking. ACM Transactions on Graphics, 41(4), Article 89, 2022. https://doi.org/10.1145/3528223.3530114",
        "[13] Xu, S.-Z., Liu, T.-Q., Liu, J.-H., Zollmann, S., Zhang, S.-H. Making resets away from points of interest in redirected walking. IEEE Transactions on Visualization and Computer Graphics, 2023.",
        "[14] Fan, C.-W., Xu, S.-Z., Yu, P., Zhang, F.-L., Zhang, S.-H. Redirected walking based on user historical walking data. Computers & Graphics, 2024.",
        "[15] Xu, S.-Z., Huang, K., Fan, C.-W., Zhang, S.-H. SafeRDW: Keep VR users safe when jumping. IEEE Transactions on Visualization and Computer Graphics, 2024.",
        "[16] Azmandian, M., Yahata, R., Grechkin, T., Thomas, J., Rosenberg, E. S. Validating simulation-based evaluation of redirected walking systems. IEEE Transactions on Visualization and Computer Graphics, 22(4), 2016.",
        "[17] Li, Y.-J., Wang, M., Steinicke, F., Zhao, Q. OpenRDW: A redirected walking library and benchmark with multi-user support. IEEE Transactions on Visualization and Computer Graphics, 2024.",
    ]
    for ref in references:
        add_para(doc, ref)


def main():
    doc = Document()
    style_document(doc)
    add_front_matter(doc)
    add_introduction(doc)
    add_related_work(doc)
    add_problem(doc)
    add_method(doc)
    add_experiment(doc)
    add_results_placeholders(doc)
    add_discussion_conclusion(doc)
    doc.save(OUT)
    print(OUT)


if __name__ == "__main__":
    main()
