if ("IntersectionObserver" in window && !matchMedia("(prefers-reduced-motion: reduce)").matches) {
  const reveal = new IntersectionObserver((entries, observer) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      entry.target.classList.add("visible");
      observer.unobserve(entry.target);
    }
  }, { threshold: 0.13 });

  document.documentElement.classList.add("motion-ready");
  document.querySelectorAll("[data-reveal]").forEach(element => reveal.observe(element));
}
