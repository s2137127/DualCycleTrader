import datetime as dt
import unittest
from unittest.mock import patch

import collect_daily as collector


class CollectorTests(unittest.TestCase):
    def test_market_index_uses_browser_document_id(self):
        url = collector.document_url("https://firestore.example/documents", "^TWII", 2026)
        self.assertTrue(url.endswith("/%255ETWII_D_2026"))

    def test_patch_preserves_other_years_and_existing_bars(self):
        old = {"Time": "2026-10-08T13:30:00", "Open": 10, "High": 11,
               "Low": 9, "Close": 10, "Volume": 100}
        new = {"Time": "2026-10-09T13:30:00", "Open": 11, "High": 12,
               "Low": 10, "Close": 11, "Volume": 150}
        previous_year = {**old, "Time": "2025-12-31T13:30:00"}
        document = {"fields": {"bars": {"mapValue": {"fields": {
            collector.candle_key(old): collector.candle_value(old)
        }}}}}
        writes = []

        def fake_request(url, *, method="GET", body=None, attempts=4):
            if method == "GET":
                return document
            writes.append((url, body))
            return {}

        with patch.object(collector, "request", side_effect=fake_request), patch.object(
            collector, "chart", return_value=[previous_year, old, new]
        ):
            result = collector.update_symbol("https://firestore.example/documents", "https://worker.example",
                                             "0050.TW", dt.date(2026, 10, 9), 2026, False)

        self.assertEqual(result, "updated")
        self.assertEqual(len(writes), 1)
        self.assertIn("bars.%60202610091330%60", writes[0][0])
        self.assertEqual(list(writes[0][1]["fields"]["bars"]["mapValue"]["fields"]),
                         [collector.candle_key(new)])

    def test_current_day_is_refetched_for_second_run(self):
        bar = {"Time": "2026-10-09T13:15:00", "Open": 10, "High": 11,
               "Low": 9, "Close": 10, "Volume": 100}
        document = {"fields": {"bars": {"mapValue": {"fields": {
            collector.candle_key(bar): collector.candle_value(bar)
        }}}}}
        with patch.object(collector, "request", return_value=document), patch.object(
            collector, "chart", return_value=[bar]
        ) as chart:
            result = collector.update_symbol("https://firestore.example/documents", "https://worker.example",
                                             "0050.TW", dt.date(2026, 10, 9), 2026, True)
        self.assertEqual(result, "current")
        chart.assert_called_once()


if __name__ == "__main__":
    unittest.main()
