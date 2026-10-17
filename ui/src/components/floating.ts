import { useLayoutEffect, useState, type RefObject } from 'react'

export interface FloatingPosition {
  top: number
  left: number
  /** 实际放在锚点的哪一侧 */
  placement: 'bottom' | 'top'
}

export interface Rect {
  top: number
  left: number
  bottom: number
  right: number
}

/** 与窗口边缘保持的最小距离 */
export const EDGE_GAP = 8
/** 浮层与锚点之间的距离 */
export const ANCHOR_GAP = 6

// 计算浮层位置（视口坐标，配合 position: fixed）：
// 默认在锚点下方、左对齐；下方放不下且上方空间更大时翻到上方；左右超出窗口时向内收；都放不下时贴住窗口边缘。
export function computeFloatingPosition(anchor: Rect, size: { width: number; height: number }, viewport: { width: number; height: number }): FloatingPosition {
  const below = anchor.bottom + ANCHOR_GAP
  const above = anchor.top - ANCHOR_GAP - size.height
  const fitsBelow = below + size.height <= viewport.height - EDGE_GAP
  const fitsAbove = above >= EDGE_GAP
  const roomBelow = viewport.height - anchor.bottom
  const roomAbove = anchor.top
  const placement: FloatingPosition['placement'] = !fitsBelow && (fitsAbove || roomAbove > roomBelow) ? 'top' : 'bottom'

  let top = placement === 'bottom' ? below : above
  top = Math.max(EDGE_GAP, Math.min(top, viewport.height - EDGE_GAP - size.height))

  let left = anchor.left
  left = Math.min(left, viewport.width - EDGE_GAP - size.width)
  left = Math.max(EDGE_GAP, left)
  return { top, left, placement }
}

// 打开期间跟随锚点：窗口缩放和任意祖先滚动时重新计算
export function useFloatingPosition(
  open: boolean,
  anchorRef: RefObject<HTMLElement | null>,
  floatingRef: RefObject<HTMLElement | null>,
): FloatingPosition | null {
  const [pos, setPos] = useState<FloatingPosition | null>(null)

  useLayoutEffect(() => {
    if (!open) return
    const update = () => {
      const anchor = anchorRef.current
      const floating = floatingRef.current
      if (!anchor || !floating) return
      const r = anchor.getBoundingClientRect()
      const next = computeFloatingPosition(
        { top: r.top, left: r.left, bottom: r.bottom, right: r.right },
        { width: floating.offsetWidth, height: floating.offsetHeight },
        { width: window.innerWidth, height: window.innerHeight },
      )
      setPos((prev) => (prev && prev.top === next.top && prev.left === next.left && prev.placement === next.placement ? prev : next))
    }
    update()
    window.addEventListener('resize', update)
    // capture：.viewer__top 等任意祖先滚动都能收到
    window.addEventListener('scroll', update, true)
    return () => {
      window.removeEventListener('resize', update)
      window.removeEventListener('scroll', update, true)
    }
  }, [open, anchorRef, floatingRef])

  // 关闭后不再使用上一次的位置；重新打开时布局效果会在绘制前算出新位置
  return open ? pos : null
}
